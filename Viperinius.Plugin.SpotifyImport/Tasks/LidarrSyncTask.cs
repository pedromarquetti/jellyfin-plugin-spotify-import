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

            // collect unique artist names from all albums
            var artistNames = albumList.Select(a => a.ArtistName ?? string.Empty)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger.LogInformation("Found {Count} unique artists", artistNames.Count);

            using var dbRepo = new DbRepository(Plugin.Instance!.DbPath);
            dbRepo.InitDb();

            // ===================================================================
            // Phase 1: Resolve all artists in Lidarr (add + monitor)
            // ===================================================================
            _logger.LogInformation("===== Phase 1: Ensuring all artists exist in Lidarr =====");

            var resolvedArtists = new Dictionary<string, LidarrArtist>(StringComparer.OrdinalIgnoreCase);
            var failedArtists = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var totalSteps = artistNames.Count + albumList.Count;
            var stepsDone = 0;

            // Pre-fetch all library artists for fast checking
            var allLibraryArtists = await lidarrService.ListAvailableArtists().ConfigureAwait(false);
            var libraryArtistByName = new Dictionary<string, LidarrArtist>(StringComparer.OrdinalIgnoreCase);
            if (allLibraryArtists != null)
            {
                foreach (var libraryArtist in allLibraryArtists)
                {
                    if (!string.IsNullOrWhiteSpace(libraryArtist.ArtistName) && !libraryArtistByName.ContainsKey(libraryArtist.ArtistName))
                    {
                        libraryArtistByName[libraryArtist.ArtistName] = libraryArtist;
                    }
                }

                _logger.LogInformation("Pre-fetched {Count} library artists", libraryArtistByName.Count);
            }

            foreach (var artistName in artistNames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stepsDone++;

                _logger.LogInformation("[{Step}/{Total}] > Artist: {Artist}", stepsDone, totalSteps, artistName);

                // Try to resolve artist from pre-fetch first, then from search lookup
                LidarrArtist? lidarrArtist = null;

                if (libraryArtistByName.TryGetValue(artistName, out var preFetched))
                {
                    lidarrArtist = preFetched;
                }

                if (lidarrArtist == null || !(lidarrArtist.Id.HasValue && lidarrArtist.Id.Value > 0))
                {
                    // Not found in pre-fetch — search Lidarr
                    var searchResults = await lidarrService.SearchArtist(artistName).ConfigureAwait(false);
                    if (searchResults != null)
                    {
                        // If any result has an Id, the artist is already in library (under different name)
                        lidarrArtist = searchResults.FirstOrDefault(a => a.Id.HasValue && a.Id.Value > 0);
                        if (lidarrArtist == null)
                        {
                            // Not in library — use first result for ForeignArtistId
                            lidarrArtist = searchResults[0];
                        }
                    }
                }

                if (lidarrArtist != null && lidarrArtist.Id.HasValue && lidarrArtist.Id.Value > 0)
                {
                    // Artist is in library
                    if (lidarrArtist.Monitored)
                    {
                        _logger.LogInformation("-> Artist already in library and monitored (id={Id}), skipping", lidarrArtist.Id.Value);
                        resolvedArtists[artistName] = lidarrArtist;
                        progress.Report((double)stepsDone / totalSteps * 100);
                        continue;
                    }

                    // In library but not monitored — set monitored (with retries)
                    _logger.LogInformation("-> Artist in library (id={Id}) but not monitored, setting monitored", lidarrArtist.Id.Value);

                    int retries = 0;
                    while (!lidarrArtist.Monitored && retries < 3)
                    {
                        if (retries > 0)
                        {
                            var refreshed = await lidarrService.SearchArtist(artistName).ConfigureAwait(false);
                            var found = refreshed?.FirstOrDefault(a => a.Id.HasValue && a.Id.Value > 0);
                            if (found == null)
                            {
                                break;
                            }

                            lidarrArtist = found;
                        }

                        if (!lidarrArtist.Monitored)
                        {
                            _logger.LogInformation("--> Setting artist with id {Id} as monitored (attempt {Retry})", lidarrArtist.Id!, retries + 1);

                            var updated = await lidarrService.SetArtistMonitored(lidarrArtist.Id!.Value, true).ConfigureAwait(false);
                            if (!updated)
                            {
                                _logger.LogWarning("SetArtistMonitored returned failure for {Artist}", artistName);
                            }
                        }

                        retries++;
                    }

                    if (!lidarrArtist.Monitored)
                    {
                        _logger.LogWarning("Failed to confirm artist {Artist} is monitored", artistName);
                    }

                    resolvedArtists[artistName] = lidarrArtist;
                    progress.Report((double)stepsDone / totalSteps * 100);
                    continue;
                }

                // Artist not in Lidarr library at all — need to add
                var foreignArtistId = lidarrArtist?.ForeignArtistId;

                if (string.IsNullOrWhiteSpace(foreignArtistId))
                {
                    _logger.LogInformation("-> Artist not found in Lidarr, resolving MusicBrainz ID...");

                    // last resort: search albums for the artist's MBID
                    var artistLookup = await lidarrService.SearchAlbum(artistName).ConfigureAwait(false);
                    if (artistLookup != null)
                    {
                        foreach (var al in artistLookup)
                        {
                            if (al.Artist != null && !string.IsNullOrWhiteSpace(al.Artist.ForeignArtistId))
                            {
                                foreignArtistId = al.Artist.ForeignArtistId;
                                break;
                            }
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(foreignArtistId))
                {
                    _logger.LogWarning("Could not determine MusicBrainz artist ID for {Artist}; skipping all albums by this artist", artistName);
                    failedArtists.Add(artistName);
                    progress.Report((double)stepsDone / totalSteps * 100);
                    continue;
                }

                _logger.LogInformation("-> Adding artist {Artist} to Lidarr", artistName);
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

                if (lidarrArtist == null || !(lidarrArtist.Id.HasValue && lidarrArtist.Id.Value > 0))
                {
                    _logger.LogError("Failed to add artist {Artist} to Lidarr; skipping all albums by this artist", artistName);
                    failedArtists.Add(artistName);
                    progress.Report((double)stepsDone / totalSteps * 100);
                    continue;
                }

                // Wait for Lidarr's post-add processing (AlbumMonitoredService
                // runs ~7s after add and unmonitors everything when Monitor="none").
                // After it finishes, we re-apply Monitored=true so it sticks.
                // BUG: check this logic, some artists / albums are being left unmonitored.
                _logger.LogInformation("--> Waiting for post-add processing for \"{Artist}\"", lidarrArtist.ArtistName);
                await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);

                // Re-fetch current state after post-add processing
                var postAddResults = await lidarrService.SearchArtist(artistName).ConfigureAwait(false);
                var postAddArtist = postAddResults?.FirstOrDefault(a => a.Id.HasValue && a.Id.Value > 0);
                if (postAddArtist != null)
                {
                    lidarrArtist = postAddArtist;
                }

                // ensure the newly added artist is monitored
                int retryCount = 0;
                while (!lidarrArtist.Monitored && retryCount < 3)
                {
                    if (retryCount > 0)
                    {
                        postAddResults = await lidarrService.SearchArtist(artistName).ConfigureAwait(false);
                        postAddArtist = postAddResults?.FirstOrDefault(a => a.Id.HasValue && a.Id.Value > 0);
                        if (postAddArtist == null)
                        {
                            _logger.LogWarning("Could not re-fetch artist {Artist} after add", artistName);
                            break;
                        }

                        lidarrArtist = postAddArtist;
                    }

                    if (!lidarrArtist.Monitored)
                    {
                        _logger.LogInformation("--> Setting newly added artist \"{Artist}\" (id: {Id}) as monitored (attempt {Retry})", lidarrArtist.ArtistName, lidarrArtist.Id!, retryCount + 1);

                        var updated = await lidarrService.SetArtistMonitored(lidarrArtist.Id!.Value, true).ConfigureAwait(false);
                        if (!updated)
                        {
                            _logger.LogWarning("SetArtistMonitored returned failure for {Artist}", artistName);
                        }
                    }

                    retryCount++;
                }

                if (lidarrArtist != null && !lidarrArtist.Monitored)
                {
                    _logger.LogWarning("Failed to set artist {Artist} as monitored after retries", artistName);
                }

                if (lidarrArtist != null)
                {
                    resolvedArtists[artistName] = lidarrArtist;
                }

                progress.Report((double)stepsDone / totalSteps * 100);
            }

            // ===================================================================
            // Phase 2: Process all albums (find + monitor + search)
            // ===================================================================
            _logger.LogInformation("===== Phase 2: Processing albums =====");

            foreach (var album in albumList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stepsDone++;

                var artistName = album.ArtistName ?? string.Empty;

                if (failedArtists.Contains(artistName))
                {
                    _logger.LogWarning("Skipping album {Album} by failed artist {Artist}", album.AlbumName, artistName);
                    progress.Report((double)stepsDone / totalSteps * 100);
                    continue;
                }

                if (!resolvedArtists.TryGetValue(artistName, out var lidarrArtist))
                {
                    _logger.LogWarning("Artist {Artist} not resolved; skipping album {Album}", artistName, album.AlbumName);
                    progress.Report((double)stepsDone / totalSteps * 100);
                    continue;
                }

                _logger.LogInformation("[{Step}/{Total}] > Album: {Artist} - {Album}", stepsDone, totalSteps, artistName, album.AlbumName);

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

                // try to find album via MusicBrainz release group ID lookup
                if (releaseGroupIds.Count > 0)
                {
                    _logger.LogInformation("-> Looking up by MusicBrainz release group ID");
                    var mbResults = await lidarrService.LookupAlbum(releaseGroupIds.First()).ConfigureAwait(false);
                    if (mbResults != null)
                    {
                        foreach (var res in mbResults)
                        {
                            if (res.Id > 0 && lidarrArtist != null
                                && MatchesArtist(res, lidarrArtist)
                                && string.Equals(res.Title, album.AlbumName, StringComparison.OrdinalIgnoreCase))
                            {
                                _logger.LogInformation("-> Found album via MusicBrainz ID: {Album}", res.Title);
                                targetAlbum = res;
                                break;
                            }
                        }
                    }
                }

                // search by name
                if (targetAlbum == null && lidarrConfig.SearchByAlbumName)
                {
                    var query = $"{album.ArtistName} {album.AlbumName}";
                    _logger.LogInformation("-> Searching by name: {Query}", query);
                    var nameResult = await lidarrService.SearchAlbum(query).ConfigureAwait(false);
                    if (nameResult != null && lidarrArtist != null)
                    {
                        foreach (var res in nameResult)
                        {
                            if (MatchesArtist(res, lidarrArtist) && string.Equals(res.Title, album.AlbumName, StringComparison.OrdinalIgnoreCase))
                            {
                                _logger.LogInformation("-> Found album via name search: {Album}", res.Title);
                                targetAlbum = res;
                                break;
                            }
                        }
                    }
                }

                // list all albums for the artist and match by name
                if (targetAlbum == null && lidarrArtist != null && lidarrArtist.Id.HasValue && lidarrArtist.Id.Value > 0)
                {
                    var artistAlbums = await lidarrService.GetArtistAlbums(lidarrArtist.Id.Value).ConfigureAwait(false);
                    _logger.LogInformation("-> Searching through {Count} artist albums", artistAlbums.Count);
                    targetAlbum = artistAlbums.FirstOrDefault(a =>
                        string.Equals(a.Title, album.AlbumName, StringComparison.OrdinalIgnoreCase));
                    if (targetAlbum != null)
                    {
                        _logger.LogInformation("-> Found album in artist's album list: {Album}", album.AlbumName);
                    }
                }

                if (targetAlbum != null)
                {
                    _logger.LogInformation("-> Album found (id={Id}), setting as monitored", targetAlbum.Id);
                    var monitored = await lidarrService.SetAlbumsMonitored(
                        new List<int> { targetAlbum.Id }, true).ConfigureAwait(false);
                    if (!monitored)
                    {
                        _logger.LogWarning("Failed to set album {Album} as monitored", album.AlbumName);
                    }

                    await lidarrService.SendCommand(new LidarrCommandRequest
                    {
                        Name = "AlbumSearch",
                        AlbumIds = new List<int> { targetAlbum.Id },
                    }).ConfigureAwait(false);
                    _logger.LogInformation("-> Album search triggered for {Album}", targetAlbum.Title);
                }
                else
                {
                    _logger.LogInformation("-> Album not found in Lidarr, attempting to add it");

                    var foreignAlbumId = string.Empty;
                    if (releaseGroupIds.Count > 0)
                    {
                        var mbLookups = await lidarrService.LookupAlbum(releaseGroupIds.First()).ConfigureAwait(false);
                        foreignAlbumId = mbLookups?.FirstOrDefault()?.ForeignAlbumId ?? string.Empty;
                    }

                    if (string.IsNullOrWhiteSpace(foreignAlbumId) && lidarrConfig.SearchByAlbumName)
                    {
                        var nameLookup = await lidarrService.SearchAlbum($"{album.ArtistName} {album.AlbumName}").ConfigureAwait(false);
                        if (nameLookup != null)
                        {
                            foreach (var res in nameLookup)
                            {
                                if (!string.IsNullOrWhiteSpace(res.Title) && string.Equals(res.Title, album.AlbumName, StringComparison.OrdinalIgnoreCase))
                                {
                                    foreignAlbumId = res.ForeignAlbumId;
                                    break;
                                }
                            }
                        }
                    }

                    if (string.IsNullOrWhiteSpace(foreignAlbumId))
                    {
                        _logger.LogWarning("Could not find MusicBrainz album ID for {Album} by {Artist}; skipping", album.AlbumName, album.ArtistName);
                        progress.Report((double)stepsDone / totalSteps * 100);
                        continue;
                    }

                    _logger.LogInformation("-> Adding album {Album} to Lidarr", album.AlbumName);
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
                        _logger.LogInformation("-> Album added and search triggered for {Album}", album.AlbumName);
                    }
                }

                progress.Report((double)stepsDone / totalSteps * 100);
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
