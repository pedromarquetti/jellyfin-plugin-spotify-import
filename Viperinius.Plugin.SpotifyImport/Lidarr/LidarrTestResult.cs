namespace Viperinius.Plugin.SpotifyImport.Lidarr
{
    /// <summary>
    /// Result of testing a Lidarr connection.
    /// </summary>
    public class LidarrTestResult
    {
        /// <summary>
        /// Gets or sets a value indicating whether the connection test was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Gets or sets a message describing the result.
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// Gets or sets the Lidarr version if available.
        /// </summary>
        public string? Version { get; set; }
    }
}
