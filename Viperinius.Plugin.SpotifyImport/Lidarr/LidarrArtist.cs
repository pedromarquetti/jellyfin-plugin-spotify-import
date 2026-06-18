using System.Text.Json.Serialization;

namespace Viperinius.Plugin.SpotifyImport.Lidarr
{
    internal class LidarrArtist
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("artistName")]
        public string? ArtistName { get; set; }

        [JsonPropertyName("foreignArtistId")]
        public string? ForeignArtistId { get; set; }

        [JsonPropertyName("monitored")]
        public bool Monitored { get; set; }

        [JsonPropertyName("rootFolderPath")]
        public string? RootFolderPath { get; set; }

        [JsonPropertyName("qualityProfileId")]
        public int QualityProfileId { get; set; }

        [JsonPropertyName("metadataProfileId")]
        public int MetadataProfileId { get; set; }

        [JsonPropertyName("path")]
        public string? Path { get; set; }

        [JsonPropertyName("addOptions")]
        public LidarrAddArtistOptions? AddOptions { get; set; }
    }
}
