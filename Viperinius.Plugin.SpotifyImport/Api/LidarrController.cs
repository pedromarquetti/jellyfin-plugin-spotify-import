using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Viperinius.Plugin.SpotifyImport.Lidarr;

namespace Viperinius.Plugin.SpotifyImport.Api
{
    /// <summary>
    /// API controller for Lidarr integration endpoints.
    /// </summary>
    [ApiController]
    [Produces(MediaTypeNames.Application.Json)]
    [Authorize]
    public class LidarrController : ControllerBase
    {
        private readonly ILoggerFactory _loggerFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="LidarrController"/> class.
        /// </summary>
        /// <param name="loggerFactory">Instance of the <see cref="ILoggerFactory"/> interface.</param>
        public LidarrController(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
        }

        /// <summary>
        /// Tests the connection to a Lidarr instance.
        /// </summary>
        /// <param name="url">The Lidarr server URL.</param>
        /// <param name="apiKey">The Lidarr API key.</param>
        /// <returns>A <see cref="LidarrTestResult"/> indicating success or failure.</returns>
        [HttpPost($"{nameof(Viperinius)}.{nameof(Viperinius.Plugin)}.{nameof(SpotifyImport)}/Lidarr/TestConnection")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<LidarrTestResult>> TestConnection(
            [FromQuery, Required] string url,
            [FromQuery, Required] string apiKey)
        {
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(apiKey))
            {
                return BadRequest(new LidarrTestResult { Success = false, Message = "URL and API key are required" });
            }

            var service = new LidarrService(url, apiKey, _loggerFactory.CreateLogger<LidarrService>());
            var result = await service.TestConnection(url, apiKey).ConfigureAwait(false);
            return Ok(result);
        }
    }
}
