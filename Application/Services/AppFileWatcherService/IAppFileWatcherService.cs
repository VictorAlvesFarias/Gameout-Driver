using Domain.Queues.AppFileDtos;

namespace Application.Services.AppFileWatcherService
{
    public interface IAppFileWatcherService
    {
        void SetWatchers();
        void SingleSync(AppFileUpdateRequestMessage req);
        Task ProcessSingleSync(int appStoredFileId, string path);
        Task IsProcessing(AppFileStatusCheckRequestMessage req);
    }
}
