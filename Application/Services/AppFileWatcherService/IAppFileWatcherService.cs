using Domain.Queues.AppFileDtos;

namespace Drivers.Services.AppFileWatcherService
{
    public interface IAppFileWatcherService
    {
        void SetWatchers(AppFileSetEventsRequestMessage req);
        void SingleSync(AppFileUpdateRequestMessage req);
        void IsProcessing(AppFileStatusCheckRequestMessage req);
        void ValidateSync(AppFileValidateStatusRequest req);
    }
}
