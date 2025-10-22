namespace Domain.Queues.AppFileDtos
{
    public class AppFileUpdateResponseMessag
    {
        public int AppStoredFileId { get; set; }

        public byte[] MemoryStream { get; set; }

        public long UncompressedSize { get; set; }
    }
}
