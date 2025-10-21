namespace Domain.Entitites.ApplicationContext
{
    public class AppFileWatcher
    {
        public int AppFileId { get; set; }
        public FileSystemWatcher FileSystemWatcher { get; set; }
    }
}
