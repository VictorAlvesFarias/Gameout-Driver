using Domain.Entitites.ApplicationContext;

namespace Infrastructure.Context
{
    public class ApplicationContext
    {
        public List<AppFileWatcher> AppFileWatchers { get; set; }

        public ApplicationContext()
        {
            AppFileWatchers = new List<AppFileWatcher>();
        }
    }
}
