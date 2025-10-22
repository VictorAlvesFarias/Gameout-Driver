namespace Domain.Queues.AppFileDtos
{
    public class AppFileErrorMessage
    {
        public int AppStoredFileId { get; set; }
        public string Mensagem { get; set; }
        public string Error { get; set; }
    }
}
