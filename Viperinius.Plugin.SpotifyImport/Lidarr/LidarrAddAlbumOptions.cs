using System.Text.Json.Serialization;

namespace Viperinius.Plugin.SpotifyImport.Lidarr
{
    internal class LidarrAddAlbumOptions
    {
        [JsonPropertyName("searchForMissingAlbums")]
        public bool SearchForMissingAlbums { get; set; }
    }
}
