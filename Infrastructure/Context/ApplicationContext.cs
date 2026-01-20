using Domain.Entitites.ApplicationContext;
using Domain.Queues.AppFileDtos;

namespace Infrastructure.Context
{
    public class ApplicationContext
    {
        public List<AppFileWatcher> AppFileWatchers { get; set; }
        public AppFileProcessingQueueItem? CurrentAppFileProcessingQueueItem { get; set; }
        
        public ApplicationContext()
        {
            AppFileWatchers = new List<AppFileWatcher>();
        }
    }
}
