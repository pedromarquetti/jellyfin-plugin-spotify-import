namespace Viperinius.Plugin.SpotifyImport.Lidarr
{
    /// <summary>
    /// Configuration for the Lidarr integration.
    /// </summary>
    public class LidarrConfiguration
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LidarrConfiguration"/> class.
        /// </summary>
        public LidarrConfiguration()
        {
            Url = string.Empty;
            ApiKey = string.Empty;
            RootFolderPath = string.Empty;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the Lidarr integration is enabled.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Gets or sets the Lidarr server URL.
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// Gets or sets the Lidarr API key.
        /// </summary>
        public string ApiKey { get; set; }

        /// <summary>
        /// Gets or sets the root folder path for Lidarr downloads.
        /// </summary>
        public string RootFolderPath { get; set; }

        /// <summary>
        /// Gets or sets the quality profile ID to use in Lidarr.
        /// </summary>
        public int QualityProfileId { get; set; } = 1;

        /// <summary>
        /// Gets or sets the metadata profile ID to use in Lidarr.
        /// </summary>
        public int MetadataProfileId { get; set; } = 1;

        /// <summary>
        /// Gets or sets a value indicating whether to search by album name as fallback.
        /// </summary>
        public bool SearchByAlbumName { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether automatic Lidarr sync is enabled.
        /// </summary>
        public bool AutoSync { get; set; }
    }
}
