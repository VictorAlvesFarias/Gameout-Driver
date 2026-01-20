using Domain.Queues.AppFileDtos;

namespace Application.Services.AppFileWatcherService
{
    public interface IAppFileService : IDisposable
    {
        void SetWatchers();
        void SingleSync(AppFileUpdateRequestMessage req);
        Task CheckStatus(AppFileStatusCheckRequestMessage req);
        Task CheckAppFileStatusAll(AppFileStatusCheckAllRequestMessage req);
    }
}
