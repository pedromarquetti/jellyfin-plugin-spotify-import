using System.Text.Json.Serialization;

namespace Viperinius.Plugin.SpotifyImport.Lidarr
{
    /// <summary>
    /// Represents a metadata profile in Lidarr.
    /// </summary>
    public class LidarrMetadataProfile
    {
        /// <summary>
        /// Gets or sets the profile ID.
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the profile name.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }
}
