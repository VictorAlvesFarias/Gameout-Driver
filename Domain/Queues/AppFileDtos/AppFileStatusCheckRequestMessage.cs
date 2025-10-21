namespace Domain.Queues.AppFileDtos
{
    public class AppFileStatusCheckRequestMessage
    {
        public int AppStoredFileId { get; set; }
        public string RequestId { get; set; }
    }
}
