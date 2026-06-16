using System.Text.Json.Serialization;

namespace Viperinius.Plugin.SpotifyImport.Lidarr
{
    internal class LidarrAddArtistOptions
    {
        [JsonPropertyName("monitor")]
        public string? Monitor { get; set; }

        [JsonPropertyName("searchForNewAlbum")]
        public bool SearchForNewAlbum { get; set; }
    }
}
