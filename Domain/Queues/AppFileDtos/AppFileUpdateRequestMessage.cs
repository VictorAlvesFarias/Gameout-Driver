namespace Domain.Queues.AppFileDtos
{
    public class AppFileUpdateRequestMessage
    {
        public int AppStoredFileId { get; set; }
        public int AppFileId { get; set; }
        public string Path { get; set; }
        public int? TraceId { get; set; }
    }
}
