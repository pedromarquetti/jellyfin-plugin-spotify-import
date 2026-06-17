using System.Text.Json.Serialization;

namespace Viperinius.Plugin.SpotifyImport.Lidarr
{
    /// <summary>
    /// Represents a root folder in Lidarr.
    /// </summary>
    public class LidarrRootFolder
    {
        /// <summary>
        /// Gets or sets the root folder ID.
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the root folder path.
        /// </summary>
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether the folder is accessible.
        /// </summary>
        [JsonPropertyName("accessible")]
        public bool Accessible { get; set; }

        /// <summary>
        /// Gets or sets the available free space in bytes.
        /// </summary>
        [JsonPropertyName("freeSpace")]
        public long? FreeSpace { get; set; }
    }
}
