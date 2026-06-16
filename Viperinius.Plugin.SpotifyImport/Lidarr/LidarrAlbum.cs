#pragma warning disable CA1002
#pragma warning disable CA2227

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Viperinius.Plugin.SpotifyImport.Lidarr
{
    internal class LidarrAlbum
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("foreignAlbumId")]
        public string? ForeignAlbumId { get; set; }

        [JsonPropertyName("artist")]
        public LidarrArtist? Artist { get; set; }

        [JsonPropertyName("artistId")]
        public int ArtistId { get; set; }

        [JsonPropertyName("foreignArtistId")]
        public string? ForeignArtistId { get; set; }

        [JsonPropertyName("monitored")]
        public bool Monitored { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("profileId")]
        public int ProfileId { get; set; }

        [JsonPropertyName("rootFolderPath")]
        public string? RootFolderPath { get; set; }

        [JsonPropertyName("qualityProfileId")]
        public int QualityProfileId { get; set; }

        [JsonPropertyName("metadataProfileId")]
        public int MetadataProfileId { get; set; }

        [JsonPropertyName("addOptions")]
        public LidarrAddAlbumOptions? AddOptions { get; set; }

        [JsonPropertyName("albumType")]
        public string? AlbumType { get; set; }

        [JsonPropertyName("albumTypeId")]
        public string? AlbumTypeId { get; set; }

        [JsonPropertyName("disambiguation")]
        public string? Disambiguation { get; set; }

        [JsonPropertyName("overview")]
        public string? Overview { get; set; }

        [JsonPropertyName("duration")]
        public int Duration { get; set; }

        [JsonPropertyName("genres")]
        public List<string> Genres { get; set; } = new();
    }
}
