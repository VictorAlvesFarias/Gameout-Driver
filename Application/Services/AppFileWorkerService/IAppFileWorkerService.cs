using Domain.Queues.AppFileDtos;

namespace Application.Services.AppFileWatcherService
{
    public interface IAppFileWorkerService
    {
        Task ProcessSingleSync(AppFileProcessingQueueItem queueItem);
    }
}
