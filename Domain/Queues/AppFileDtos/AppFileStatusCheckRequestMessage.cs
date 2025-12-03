namespace Domain.Queues.AppFileDtos
{
    public class AppFileStatusCheckRequestMessage
    {
        public int AppStoredFileId { get; set; }
        public string Path { get; set; }
        public int? TraceId { get; set; }
    }
}

