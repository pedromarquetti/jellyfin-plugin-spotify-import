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
using Viperinius.Plugin.SpotifyImport.Utils;

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

            // collect unique albums and their ISRCs from all missing track files
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
                        if (!uniqueAlbums.TryGetValue(key, out var albumData))
                        {
                            albumData = new AlbumData
                            {
                                ArtistName = artist,
                                AlbumName = track.AlbumName,
                            };
                            uniqueAlbums[key] = albumData;
                        }

                        if (!string.IsNullOrWhiteSpace(track.IsrcId))
                        {
                            albumData.Isrcs.Add(track.IsrcId);
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

            var albumList = uniqueAlbums.Values.ToList();
            var albumNames = string.Join(", ", albumList.Select(a => $"{a.ArtistName} - {a.AlbumName}"));
            _logger.LogInformation("Found {Count} unique albums: {AlbumList}", albumList.Count, albumNames);

            var processed = 0;
            var resolvedArtists = new Dictionary<string, LidarrArtist>(StringComparer.OrdinalIgnoreCase);

            using var dbRepo = new DbRepository(Plugin.Instance!.DbPath);
            dbRepo.InitDb();

            foreach (var album in albumList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                processed++;

                _logger.LogInformation("Artist: {Artist} - Processing album: {Album}", album.ArtistName, album.AlbumName);

                // ===== Phase 1: Ensure artist exists in Lidarr and is monitored =====

                var artistName = album.ArtistName ?? string.Empty;
                if (!resolvedArtists.TryGetValue(artistName, out var lidarrArtist))
                {
                    lidarrArtist = await lidarrService.SearchArtist(artistName).ConfigureAwait(false);
                    if (lidarrArtist != null && lidarrArtist.Id > 0)
                    {
                        int retries = 0;
                        while (!lidarrArtist.Monitored && retries < 3)
                        {
                            if (retries > 0)
                            {
                                var refetched = await lidarrService.SearchArtist(artistName).ConfigureAwait(false);
                                if (refetched == null || refetched.Id == 0)
                                {
                                    break;
                                }

                                lidarrArtist = refetched;
                            }

                            if (!lidarrArtist.Monitored)
                            {
                                _logger.LogInformation("Artist {Artist} exists but not monitored; updating (attempt {Retry})", artistName, retries + 1);
                                lidarrArtist.Monitored = true;
                                lidarrArtist.RootFolderPath = lidarrConfig.RootFolderPath;
                                lidarrArtist.QualityProfileId = lidarrConfig.QualityProfileId;
                                lidarrArtist.MetadataProfileId = lidarrConfig.MetadataProfileId;
                                lidarrArtist.AddOptions = null;

                                var updated = await lidarrService.UpdateArtist(lidarrArtist.Id, lidarrArtist).ConfigureAwait(false);
                                if (!updated)
                                {
                                    _logger.LogWarning("UpdateArtist returned failure for {Artist}", artistName);
                                }
                            }

                            retries++;
                        }
                    }
                    else
                    { // Artist not in Lidarr.
                        // If SearchArtist returned a MusicBrainz
                        // hit (Id == 0), reuse its ForeignArtistId.
                        var foreignArtistId = lidarrArtist?.ForeignArtistId;

                        if (string.IsNullOrWhiteSpace(foreignArtistId))
                        { // Last resort: try album search for the artist's MB ID.
                            var artistLookup = await lidarrService.SearchAlbum(artistName).ConfigureAwait(false);
                            foreignArtistId = artistLookup?.Artist?.ForeignArtistId;
                        }

                        if (string.IsNullOrWhiteSpace(foreignArtistId))
                        {
                            _logger.LogWarning("Could not determine MusicBrainz artist ID for {Artist}; skipping", artistName);
                            continue;
                        }

                        _logger.LogInformation("Artist {Artist} not found in Lidarr; adding", artistName);
                        lidarrArtist = await lidarrService.AddArtist(new LidarrAddArtistRequest
                        {
                            ForeignArtistId = foreignArtistId,
                            ArtistName = artistName,
                            Monitored = true,
                            RootFolderPath = lidarrConfig.RootFolderPath,
                            QualityProfileId = lidarrConfig.QualityProfileId,
                            MetadataProfileId = lidarrConfig.MetadataProfileId,
                            AddOptions = new LidarrAddArtistOptions
                            {
                                Monitor = "none",
                                SearchForNewAlbum = false,
                            },
                        }).ConfigureAwait(false);

                        if (lidarrArtist == null || lidarrArtist.Id == 0)
                        {
                            _logger.LogError("Failed to add artist {Artist} to Lidarr", artistName);
                            continue;
                        }

                        int retries = 0;
                        while (!lidarrArtist.Monitored && retries < 3)
                        {
                            if (retries > 0)
                            {
                                lidarrArtist = await lidarrService.SearchArtist(artistName).ConfigureAwait(false);
                                if (lidarrArtist == null || lidarrArtist.Id == 0)
                                {
                                    _logger.LogWarning("Could not re-fetch artist {Artist} after add", artistName);
                                    break;
                                }
                            }

                            if (!lidarrArtist.Monitored)
                            {
                                _logger.LogInformation("Artist {Artist} added but not monitored; setting monitored (attempt {Retry})", artistName, retries + 1);
                                lidarrArtist.Monitored = true;
                                lidarrArtist.RootFolderPath = lidarrConfig.RootFolderPath;
                                lidarrArtist.QualityProfileId = lidarrConfig.QualityProfileId;
                                lidarrArtist.MetadataProfileId = lidarrConfig.MetadataProfileId;
                                lidarrArtist.AddOptions = null;

                                var updated = await lidarrService.UpdateArtist(lidarrArtist.Id, lidarrArtist).ConfigureAwait(false);
                                if (!updated)
                                {
                                    _logger.LogWarning("UpdateArtist returned failure for {Artist}", artistName);
                                }
                            }

                            retries++;
                        }

                        if (lidarrArtist != null && !lidarrArtist.Monitored)
                        {
                            _logger.LogWarning("Failed to set artist {Artist} as monitored after retries", artistName);
                        }

                        if (lidarrArtist != null)
                        {
                            resolvedArtists[artistName] = lidarrArtist;
                        }
                    }
                }

                // ===== Phase 2: Find the specific album and ensure it is monitored =====

                // resolve MusicBrainz release group ID from cached ISRC mappings
                var releaseGroupIds = new HashSet<string>();
                foreach (var isrc in album.Isrcs.Distinct())
                {
                    var mappings = dbRepo.GetIsrcMusicBrainzMapping(isrc: isrc);
                    foreach (var mapping in mappings)
                    {
                        foreach (var rgId in mapping.MusicBrainzReleaseGroupIds)
                        {
                            releaseGroupIds.Add(rgId.ToString("D"));
                        }
                    }
                }

                LidarrAlbum? targetAlbum = null;

                // 2a — try to find album via MusicBrainz release group ID lookup
                if (releaseGroupIds.Count > 0)
                {
                    var mbResult = await lidarrService.LookupAlbum(releaseGroupIds.First()).ConfigureAwait(false);
                    if (mbResult != null && mbResult.Id > 0 && lidarrArtist != null && MatchesArtist(mbResult, lidarrArtist))
                    {
                        targetAlbum = mbResult;
                    }
                }

                // 2b — search by name
                if (targetAlbum == null && lidarrConfig.SearchByAlbumName)
                {
                    var query = $"{album.ArtistName} {album.AlbumName}";
                    _logger.LogInformation("No MusicBrainz ID cached; searching by name: {Query}", query);
                    var nameResult = await lidarrService.SearchAlbum(query).ConfigureAwait(false);
                    if (nameResult != null && nameResult.Id > 0 && lidarrArtist != null && MatchesArtist(nameResult, lidarrArtist))
                    {
                        _logger.LogInformation("Found {Album} in Lidarr via name search", album.AlbumName);
                        targetAlbum = nameResult;
                    }
                }

                // 2c — list all albums for the artist and match by name
                if (targetAlbum == null && lidarrArtist != null && lidarrArtist.Id > 0)
                {
                    var artistAlbums = await lidarrService.GetArtistAlbums(lidarrArtist.Id).ConfigureAwait(false);
                    _logger.LogInformation("Artist has {Count} albums in Lidarr", artistAlbums.Count);
                    targetAlbum = artistAlbums.FirstOrDefault(a =>
                        string.Equals(a.Title, album.AlbumName, StringComparison.OrdinalIgnoreCase));
                    if (targetAlbum != null)
                    {
                        _logger.LogInformation("Found album {Album} in artist's album list", album.AlbumName);
                    }
                }

                if (targetAlbum != null)
                {
                    // album exists — ensure monitored + trigger search
                    if (!targetAlbum.Monitored)
                    {
                        _logger.LogInformation("Album {Album} exists but not monitored; updating", album.AlbumName);
                        var updated = await lidarrService.UpdateAlbum(targetAlbum.Id, new LidarrAlbumUpdateRequest
                        {
                            Id = targetAlbum.Id,
                            Monitored = true,
                            ArtistId = targetAlbum.ArtistId,
                            ForeignAlbumId = targetAlbum.ForeignAlbumId,
                            Title = targetAlbum.Title,
                            ProfileId = targetAlbum.ProfileId,
                            RootFolderPath = targetAlbum.RootFolderPath,
                        }).ConfigureAwait(false);

                        if (!updated)
                        {
                            _logger.LogWarning("Failed to update album {Album} in Lidarr", album.AlbumName);
                        }
                    }

                    await lidarrService.SendCommand(new LidarrCommandRequest
                    {
                        Name = "AlbumSearch",
                        AlbumIds = new List<int> { targetAlbum.Id },
                    }).ConfigureAwait(false);
                }
                else
                {
                    var foreignAlbumId = string.Empty;
                    if (releaseGroupIds.Count > 0)
                    {
                        var mbLookup = await lidarrService.LookupAlbum(releaseGroupIds.First()).ConfigureAwait(false);
                        foreignAlbumId = mbLookup?.ForeignAlbumId ?? string.Empty;
                    }

                    if (string.IsNullOrWhiteSpace(foreignAlbumId) && lidarrConfig.SearchByAlbumName)
                    {
                        var nameLookup = await lidarrService.SearchAlbum($"{album.ArtistName} {album.AlbumName}").ConfigureAwait(false);
                        if (nameLookup != null && !string.IsNullOrWhiteSpace(nameLookup.Title)
                            && string.Equals(nameLookup.Title, album.AlbumName, StringComparison.OrdinalIgnoreCase))
                        {
                            foreignAlbumId = nameLookup.ForeignAlbumId;
                        }
                        else if (nameLookup != null)
                        {
                            _logger.LogWarning("SearchAlbum returned {ReturnedTitle} instead of {ExpectedTitle}; not using it", nameLookup.Title, album.AlbumName);
                        }
                    }

                    if (string.IsNullOrWhiteSpace(foreignAlbumId))
                    {
                        _logger.LogWarning("Could not find MusicBrainz album ID for {Album} by {Artist}; skipping", album.AlbumName, album.ArtistName);
                        continue;
                    }

                    _logger.LogInformation("Adding album {Album} to Lidarr", album.AlbumName);
                    var addResult = await lidarrService.AddAlbum(new LidarrAddAlbumRequest
                    {
                        ForeignAlbumId = foreignAlbumId,
                        Artist = new LidarrArtist
                        {
                            Id = lidarrArtist!.Id,
                            ForeignArtistId = lidarrArtist.ForeignArtistId,
                            ArtistName = lidarrArtist.ArtistName,
                            Monitored = true,
                            QualityProfileId = lidarrConfig.QualityProfileId,
                            MetadataProfileId = lidarrConfig.MetadataProfileId,
                            RootFolderPath = lidarrConfig.RootFolderPath,
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

        private static bool MatchesArtist(LidarrAlbum album, LidarrArtist artist)
        {
            if (artist.Id > 0 && album.ArtistId == artist.Id)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(artist.ForeignArtistId)
                && string.Equals(album.ForeignArtistId, artist.ForeignArtistId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (album.Artist != null
                && !string.IsNullOrWhiteSpace(artist.ForeignArtistId)
                && string.Equals(album.Artist.ForeignArtistId, artist.ForeignArtistId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private class AlbumData
        {
            public string? ArtistName { get; set; }

            public string? AlbumName { get; set; }

            public List<string> Isrcs { get; } = new();
        }
    }
}
