using System.Threading.Tasks;

namespace Viperinius.Plugin.SpotifyImport.Lidarr
{
    internal interface ILidarrService
    {
        Task<LidarrTestResult> TestConnection(string url, string apiKey);

        Task<LidarrAlbum?> LookupAlbum(string mbReleaseGroupId);

        Task<LidarrAlbum?> GetAlbum(string foreignAlbumId);

        Task<LidarrAlbum?> AddAlbum(LidarrAddAlbumRequest request);

        Task<bool> UpdateAlbum(int id, LidarrAlbumUpdateRequest request);

        Task<LidarrCommandResponse?> SendCommand(LidarrCommandRequest command);
    }
}
