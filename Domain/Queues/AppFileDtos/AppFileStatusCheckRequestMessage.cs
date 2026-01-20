namespace Domain.Queues.AppFileDtos
{
    public class AppFileStatusCheckRequestMessage
    {
        public int AppFileId { get; set; }
        public string Path { get; set; }
    }
}

