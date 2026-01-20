namespace Domain.Queues.AppFileDtos
{
    public class AppFileUpdateRequestMessage
    {
        public int AppFileId { get; set; }
        public string Path { get; set; }
    }
}
