using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Viperinius.Plugin.SpotifyImport.Lidarr
{
    internal class LidarrService : ILidarrService
    {
        private readonly string _url;
        private readonly string _apiKey;
        private readonly ILogger<LidarrService> _logger;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private static readonly HttpClient _httpClient = new(new HttpClientHandler
        {
            CheckCertificateRevocationList = true,
        });

        public LidarrService(string url, string apiKey, ILogger<LidarrService> logger)
        {
            _url = url.TrimEnd('/');
            _apiKey = apiKey;
            _logger = logger;
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string path)
        {
            var req = new HttpRequestMessage(method, $"{_url}{path}");
            req.Headers.Add("X-Api-Key", _apiKey);
            req.Headers.UserAgent.ParseAdd("Viperinius.Plugin.SpotifyImport");
            return req;
        }

        public async Task<LidarrTestResult> TestConnection(string url, string apiKey)
        {
            var testUrl = url.TrimEnd('/');
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{testUrl}/api/v1/system/status");
                req.Headers.Add("X-Api-Key", apiKey);
                req.Headers.UserAgent.ParseAdd("Viperinius.Plugin.SpotifyImport");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var response = await _httpClient.SendAsync(req, cts.Token).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    var version = doc.RootElement.TryGetProperty("version", out var ver)
                        ? ver.GetString()
                        : null;

                    return new LidarrTestResult
                    {
                        Success = true,
                        Message = "Connection successful",
                        Version = version,
                    };
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return new LidarrTestResult { Success = false, Message = "Invalid API key (unauthorized)" };
                }

                return new LidarrTestResult { Success = false, Message = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}" };
            }
            catch (TaskCanceledException)
            {
                return new LidarrTestResult { Success = false, Message = "Connection timed out" };
            }
            catch (HttpRequestException ex)
            {
                return new LidarrTestResult { Success = false, Message = $"Connection failed: {ex.Message}" };
            }
            catch (Exception ex)
            {
                return new LidarrTestResult { Success = false, Message = $"Unexpected error: {ex.Message}" };
            }
        }

        public async Task<LidarrAlbum[]?> LookupAlbum(string mbReleaseGroupId)
        {
            try
            {
                using var req = CreateRequest(HttpMethod.Get, $"/api/v1/album/lookup?term=lidarr:{mbReleaseGroupId}");
                var response = await _httpClient.SendAsync(req).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var results = JsonSerializer.Deserialize<LidarrAlbum[]>(content, _jsonOptions);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to lookup album in Lidarr with MB release group ID {Id}", mbReleaseGroupId);
                return null;
            }
        }

        public async Task<LidarrAlbum?> GetAlbum(string foreignAlbumId)
        {
            try
            {
                using var req = CreateRequest(HttpMethod.Get, $"/api/v1/album?foreignAlbumId={foreignAlbumId}");
                var response = await _httpClient.SendAsync(req).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var results = JsonSerializer.Deserialize<LidarrAlbum[]>(content, _jsonOptions);
                return results?.Length > 0 ? results[0] : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get album from Lidarr with foreign album ID {Id}", foreignAlbumId);
                return null;
            }
        }

        public async Task<LidarrAlbum?> AddAlbum(LidarrAddAlbumRequest request)
        {
            try
            {
                var json = JsonSerializer.Serialize(request, _jsonOptions);
                using var req = CreateRequest(HttpMethod.Post, "/api/v1/album");
                req.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(req).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    _logger.LogError("Failed to add album to Lidarr: HTTP {Code} - {Error}", (int)response.StatusCode, errorContent);
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JsonSerializer.Deserialize<LidarrAlbum>(content, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add album to Lidarr");
                return null;
            }
        }

        public async Task<LidarrAlbum[]?> SearchAlbum(string query)
        {
            try
            {
                using var req = CreateRequest(HttpMethod.Get, $"/api/v1/album/lookup?term={Uri.EscapeDataString(query)}");
                var response = await _httpClient.SendAsync(req).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var results = JsonSerializer.Deserialize<LidarrAlbum[]>(content, _jsonOptions);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to search album in Lidarr with query {Query}", query);
                return null;
            }
        }

        public async Task<LidarrArtist[]?> ListAvailableArtists()
        {
            try
            {
                using var req = CreateRequest(HttpMethod.Get, $"/api/v1/artist/");
                var response = await _httpClient.SendAsync(req).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var results = JsonSerializer.Deserialize<LidarrArtist[]>(content, _jsonOptions);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list artists");
                return null;
            }
        }

        public async Task<LidarrArtist[]?> SearchArtist(string query)
        {
            try
            {
                using var req = CreateRequest(HttpMethod.Get, $"/api/v1/artist/lookup?term={Uri.EscapeDataString(query)}");
                var response = await _httpClient.SendAsync(req).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var results = JsonSerializer.Deserialize<LidarrArtist[]>(content, _jsonOptions);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to search artist in Lidarr with query {Query}", query);
                return null;
            }
        }

        public async Task<LidarrArtist?> AddArtist(LidarrAddArtistRequest request)
        {
            try
            {
                var json = JsonSerializer.Serialize(request, _jsonOptions);
                using var req = CreateRequest(HttpMethod.Post, "/api/v1/artist");
                req.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(req).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    _logger.LogError("Failed to add artist to Lidarr: HTTP {Code} - {Error}", (int)response.StatusCode, errorContent);
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JsonSerializer.Deserialize<LidarrArtist>(content, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add artist to Lidarr");
                return null;
            }
        }

        public async Task<bool> SetArtistMonitored(int id, bool monitored)
        {
            try
            {
                var request = new LidarrArtistEditorRequest
                {
                    ArtistIds = new List<int> { id },
                    Monitored = monitored,
                };

                var json = JsonSerializer.Serialize(request, _jsonOptions);
                using var req = CreateRequest(HttpMethod.Put, "/api/v1/artist/editor");
                req.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(req).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set artist {Id} monitored status in Lidarr", id);
                return false;
            }
        }

        public async Task<List<LidarrAlbum>> GetArtistAlbums(int artistId)
        {
            try
            {
                using var req = CreateRequest(HttpMethod.Get, $"/api/v1/album?artistId={artistId}");
                var response = await _httpClient.SendAsync(req).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return new List<LidarrAlbum>();
                }

                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JsonSerializer.Deserialize<List<LidarrAlbum>>(content, _jsonOptions) ?? new List<LidarrAlbum>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get albums for artist {Id} from Lidarr", artistId);
                return new List<LidarrAlbum>();
            }
        }

        public async Task<LidarrRootFolder[]?> GetRootFolders()
        {
            try
            {
                using var req = CreateRequest(HttpMethod.Get, "/api/v1/rootfolder");
                var response = await _httpClient.SendAsync(req).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JsonSerializer.Deserialize<LidarrRootFolder[]>(content, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get root folders from Lidarr");
                return null;
            }
        }

        public async Task<LidarrQualityProfile[]?> GetQualityProfiles()
        {
            try
            {
                using var req = CreateRequest(HttpMethod.Get, "/api/v1/qualityprofile");
                var response = await _httpClient.SendAsync(req).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JsonSerializer.Deserialize<LidarrQualityProfile[]>(content, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get quality profiles from Lidarr");
                return null;
            }
        }

        public async Task<LidarrMetadataProfile[]?> GetMetadataProfiles()
        {
            try
            {
                using var req = CreateRequest(HttpMethod.Get, "/api/v1/metadataprofile");
                var response = await _httpClient.SendAsync(req).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JsonSerializer.Deserialize<LidarrMetadataProfile[]>(content, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get metadata profiles from Lidarr");
                return null;
            }
        }

        public async Task<bool> SetAlbumsMonitored(List<int> albumIds, bool monitored)
        {
            try
            {
                var request = new AlbumsMonitoredResource
                {
                    AlbumIds = albumIds,
                    Monitored = monitored,
                };

                var json = JsonSerializer.Serialize(request, _jsonOptions);
                using var req = CreateRequest(HttpMethod.Put, "/api/v1/album/monitor");
                req.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(req).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set albums monitored in Lidarr");
                return false;
            }
        }

        public async Task<LidarrCommandResponse?> SendCommand(LidarrCommandRequest command)
        {
            try
            {
                var json = JsonSerializer.Serialize(command, _jsonOptions);
                using var req = CreateRequest(HttpMethod.Post, "/api/v1/command");
                req.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(req).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JsonSerializer.Deserialize<LidarrCommandResponse>(content, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send command to Lidarr");
                return null;
            }
        }
    }
}
