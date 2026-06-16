using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Viperinius.Plugin.SpotifyImport.Lidarr;

namespace Viperinius.Plugin.SpotifyImport.Tasks
{
    /// <summary>
    /// Scheduled task to send missing tracks to Lidarr for download.
    /// </summary>
    public class LidarrSyncTask : IScheduledTask
    {
        private readonly ILogger<LidarrSyncTask> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILibraryManager _libraryManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="LidarrSyncTask"/> class.
        /// </summary>
        /// <param name="loggerFactory">Instance of the <see cref="ILoggerFactory"/> interface.</param>
        /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
        public LidarrSyncTask(
            ILoggerFactory loggerFactory,
            ILibraryManager libraryManager)
        {
            _logger = loggerFactory.CreateLogger<LidarrSyncTask>();
            _loggerFactory = loggerFactory;
            _libraryManager = libraryManager;
        }

        /// <inheritdoc/>
        public string Name => "Send missing tracks to Lidarr";

        /// <inheritdoc/>
        public string Key => "ViperiniusSpotifyLidarrSyncTask";

        /// <inheritdoc/>
        public string Description => "Processes missing track files and sends missing albums to Lidarr for download.";

        /// <inheritdoc/>
        public string Category => "Playlists";

        /// <inheritdoc/>
        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null)
            {
                _logger.LogError("Plugin configuration not available");
                return;
            }

            var lidarrConfig = config.Lidarr;
            if (!lidarrConfig.Enabled)
            {
                _logger.LogInformation("Lidarr integration is disabled");
                return;
            }

            if (string.IsNullOrWhiteSpace(lidarrConfig.Url) || string.IsNullOrWhiteSpace(lidarrConfig.ApiKey))
            {
                _logger.LogError("Lidarr URL or API key not configured");
                return;
            }

            _logger.LogInformation("Starting Lidarr sync");

            var lidarrService = new LidarrService(lidarrConfig.Url, lidarrConfig.ApiKey, _loggerFactory.CreateLogger<LidarrService>());

            // find missing track files
            var missingFiles = MissingTrackStore.GetFileList();
            if (missingFiles.Count == 0)
            {
                _logger.LogInformation("No missing track files found");
                progress.Report(100);
                return;
            }

            // collect unique albums from all missing track files
            var uniqueAlbums = new Dictionary<string, AlbumData>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in missingFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var json = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                    var tracks = JsonSerializer.Deserialize<List<ProviderTrackInfo>>(json);
                    if (tracks == null)
                    {
                        continue;
                    }

                    foreach (var track in tracks)
                    {
                        if (string.IsNullOrWhiteSpace(track.AlbumName))
                        {
                            continue;
                        }

                        var artist = track.AlbumArtistNames.Count > 0
                            ? track.AlbumArtistNames[0]
                            : (track.ArtistNames.Count > 0 ? track.ArtistNames[0] : "Unknown Artist");

                        var key = $"{artist}|{track.AlbumName}";
                        if (!uniqueAlbums.ContainsKey(key))
                        {
                            uniqueAlbums[key] = new AlbumData
                            {
                                ArtistName = artist,
                                AlbumName = track.AlbumName,
                            };
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to read missing track file {File}", file);
                }
            }

            if (uniqueAlbums.Count == 0)
            {
                _logger.LogInformation("No unique albums found in missing track files");
                progress.Report(100);
                return;
            }

            _logger.LogInformation("Found {Count} unique albums to send to Lidarr", uniqueAlbums.Count);

            var albumList = uniqueAlbums.Values.ToList();
            var processed = 0;

            foreach (var album in albumList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                processed++;

                _logger.LogInformation("Processing album: {Artist} - {Album}", album.ArtistName, album.AlbumName);

                // TODO: MusicBrainz ID resolution needed for proper Lidarr lookup
                // Current approach attempts lookup by album name which is a fallback
                var lookupResult = await lidarrService.LookupAlbum(album.AlbumName ?? string.Empty).ConfigureAwait(false);
                if (lookupResult == null)
                {
                    _logger.LogWarning("Could not find album {Album} by {Artist} in Lidarr via lookup; MusicBrainz ID resolution will be added in a future update", album.AlbumName, album.ArtistName);
                    continue;
                }

                // check if album already exists in Lidarr
                var existingAlbum = await lidarrService.GetAlbum(lookupResult.ForeignAlbumId ?? string.Empty).ConfigureAwait(false);
                if (existingAlbum != null)
                {
                    // album exists, just ensure it's monitored and trigger search
                    if (!existingAlbum.Monitored)
                    {
                        await lidarrService.UpdateAlbum(existingAlbum.Id, new LidarrAlbumUpdateRequest
                        {
                            Id = existingAlbum.Id,
                            Monitored = true,
                            ArtistId = existingAlbum.ArtistId,
                            ForeignAlbumId = existingAlbum.ForeignAlbumId,
                            Title = existingAlbum.Title,
                            ProfileId = existingAlbum.ProfileId,
                        }).ConfigureAwait(false);
                    }

                    await lidarrService.SendCommand(new LidarrCommandRequest
                    {
                        Name = "AlbumSearch",
                        AlbumIds = new List<int> { existingAlbum.Id },
                    }).ConfigureAwait(false);
                }
                else
                {
                    // album doesn't exist, add it
                    var addResult = await lidarrService.AddAlbum(new LidarrAddAlbumRequest
                    {
                        ForeignAlbumId = lookupResult.ForeignAlbumId,
                        Artist = new LidarrArtist
                        {
                            ForeignArtistId = lookupResult.ForeignArtistId,
                            ArtistName = lookupResult.Artist?.ArtistName ?? album.ArtistName,
                            Monitored = true,
                            RootFolderPath = lidarrConfig.RootFolderPath,
                            QualityProfileId = lidarrConfig.QualityProfileId,
                            MetadataProfileId = lidarrConfig.MetadataProfileId,
                            AddOptions = new LidarrAddArtistOptions
                            {
                                Monitor = "none",
                                SearchForNewAlbum = true,
                            },
                        },
                        Monitored = true,
                        QualityProfileId = lidarrConfig.QualityProfileId,
                        MetadataProfileId = lidarrConfig.MetadataProfileId,
                        RootFolderPath = lidarrConfig.RootFolderPath,
                        AddOptions = new LidarrAddAlbumOptions
                        {
                            SearchForMissingAlbums = true,
                        },
                    }).ConfigureAwait(false);

                    if (addResult != null)
                    {
                        await lidarrService.SendCommand(new LidarrCommandRequest
                        {
                            Name = "AlbumSearch",
                            AlbumIds = new List<int> { addResult.Id },
                        }).ConfigureAwait(false);
                    }
                }

                progress.Report((double)processed / albumList.Count * 100);
            }

            _logger.LogInformation("Lidarr sync completed");
            progress.Report(100);
        }

        /// <inheritdoc/>
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfoType.DailyTrigger,
                    TimeOfDayTicks = TimeSpan.FromHours(4).Ticks,
                    MaxRuntimeTicks = TimeSpan.FromHours(2).Ticks,
                }
            };
        }

        private class AlbumData
        {
            public string? ArtistName { get; set; }

            public string? AlbumName { get; set; }
        }
    }
}
