using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Viperinius.Plugin.SpotifyImport.Lidarr
{
    internal class AlbumsMonitoredResource
    {
        [JsonPropertyName("albumIds")]
        public List<int> AlbumIds { get; set; } = new();

        [JsonPropertyName("monitored")]
        public bool Monitored { get; set; }
    }
}
