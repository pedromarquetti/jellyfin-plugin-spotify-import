using System.Text.Json.Serialization;

namespace Viperinius.Plugin.SpotifyImport.Lidarr
{
    internal class LidarrAlbumUpdateRequest
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("monitored")]
        public bool Monitored { get; set; }

        [JsonPropertyName("artistId")]
        public int ArtistId { get; set; }

        [JsonPropertyName("foreignAlbumId")]
        public string? ForeignAlbumId { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("profileId")]
        public int ProfileId { get; set; }
    }
}
