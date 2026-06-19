using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Viperinius.Plugin.SpotifyImport.Lidarr
{
    internal class LidarrArtistEditorRequest
    {
        [JsonPropertyName("artistIds")]
        public List<int> ArtistIds { get; set; } = new();

        [JsonPropertyName("monitored")]
        public bool Monitored { get; set; }
    }
}
