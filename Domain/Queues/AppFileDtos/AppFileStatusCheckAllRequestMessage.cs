namespace Domain.Queues.AppFileDtos
{
    public class AppFileStatusCheckAllRequestMessage
    {
        public int AppFileId { get; set; }
        public string Path { get; set; }
        public double? LastSyncedFileSize { get; set; }
        public DateTime? LastSyncedFileDate { get; set; }
    }
}
