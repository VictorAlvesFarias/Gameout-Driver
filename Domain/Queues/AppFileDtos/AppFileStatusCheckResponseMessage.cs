namespace Domain.Queues.AppFileDtos
{
    public class AppFileStatusCheckResponseMessage
    {
        public int AppStoredFileId { get; set; }
        public string Error { get; set; }
        public string Message { get; set; }
    }
}
