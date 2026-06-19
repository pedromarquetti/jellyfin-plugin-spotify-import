using System.Collections.Generic;
using System.Threading.Tasks;

namespace Viperinius.Plugin.SpotifyImport.Lidarr
{
    internal interface ILidarrService
    {
        Task<LidarrTestResult> TestConnection(string url, string apiKey);

        Task<LidarrAlbum[]?> LookupAlbum(string mbReleaseGroupId);

        Task<LidarrAlbum?> GetAlbum(string foreignAlbumId);

        Task<LidarrAlbum[]?> SearchAlbum(string query);

        Task<LidarrAlbum?> AddAlbum(LidarrAddAlbumRequest request);

        Task<bool> SetAlbumsMonitored(List<int> albumIds, bool monitored);

        Task<LidarrArtist[]?> SearchArtist(string query);

        Task<LidarrArtist[]?> ListAvailableArtists();

        Task<LidarrArtist?> AddArtist(LidarrAddArtistRequest request);

        Task<bool> SetArtistMonitored(int id, bool monitored);

        Task<List<LidarrAlbum>> GetArtistAlbums(int artistId);

        Task<LidarrCommandResponse?> SendCommand(LidarrCommandRequest command);

        Task<LidarrRootFolder[]?> GetRootFolders();

        Task<LidarrQualityProfile[]?> GetQualityProfiles();

        Task<LidarrMetadataProfile[]?> GetMetadataProfiles();
    }
}
