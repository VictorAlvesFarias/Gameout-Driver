using Domain.Queues.AppFileDtos;

namespace Application.Services.AppFileWatcherService
{
    public interface IAppFileWatcherService
    {
        void SetWatchers();
        void SingleSync(AppFileUpdateRequestMessage req);
        Task ProcessSingleSync(AppFileProcessingQueueItem queueItem);
        Task IsProcessing(AppFileStatusCheckRequestMessage req);
        Task CheckAppFileStatusAll(AppFileStatusCheckAllRequestMessage req);
    }
}
