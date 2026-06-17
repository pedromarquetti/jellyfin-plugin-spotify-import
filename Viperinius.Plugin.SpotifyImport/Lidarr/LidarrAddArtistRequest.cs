using System.Text.Json.Serialization;

namespace Viperinius.Plugin.SpotifyImport.Lidarr
{
    internal class LidarrAddArtistRequest
    {
        [JsonPropertyName("foreignArtistId")]
        public string? ForeignArtistId { get; set; }

        [JsonPropertyName("artistName")]
        public string? ArtistName { get; set; }

        [JsonPropertyName("monitored")]
        public bool Monitored { get; set; }

        [JsonPropertyName("rootFolderPath")]
        public string? RootFolderPath { get; set; }

        [JsonPropertyName("qualityProfileId")]
        public int QualityProfileId { get; set; }

        [JsonPropertyName("metadataProfileId")]
        public int MetadataProfileId { get; set; }

        [JsonPropertyName("addOptions")]
        public LidarrAddArtistOptions? AddOptions { get; set; }
    }
}
