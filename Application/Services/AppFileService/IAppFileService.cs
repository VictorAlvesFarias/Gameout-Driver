using Domain.Queues.AppFileDtos;

namespace Application.Services.AppFileWatcherService
{
    public interface IAppFileService
    {
        void SetWatchers();
        void SingleSync(AppFileUpdateRequestMessage req);
        Task IsProcessing(AppFileStatusCheckRequestMessage req);
        Task CheckAppFileStatusAll(AppFileStatusCheckAllRequestMessage req);
    }
}
