using System.Text.Json.Serialization;

namespace Viperinius.Plugin.SpotifyImport.Lidarr
{
    internal class LidarrAddAlbumRequest
    {
        [JsonPropertyName("foreignAlbumId")]
        public string? ForeignAlbumId { get; set; }

        [JsonPropertyName("artist")]
        public LidarrArtist? Artist { get; set; }

        [JsonPropertyName("monitored")]
        public bool Monitored { get; set; }

        [JsonPropertyName("qualityProfileId")]
        public int QualityProfileId { get; set; }

        [JsonPropertyName("metadataProfileId")]
        public int MetadataProfileId { get; set; }

        [JsonPropertyName("rootFolderPath")]
        public string? RootFolderPath { get; set; }

        [JsonPropertyName("addOptions")]
        public LidarrAddAlbumOptions? AddOptions { get; set; }
    }
}
