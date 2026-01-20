namespace Domain.Queues.AppFileDtos
{
    /// <summary>
    /// Item da fila de processamento interno - inclui trace id
    /// </summary>
    public class AppFileProcessingQueueItem
    {
        public int AppFileId { get; set; }
        public string Path { get; set; }
        public string? TraceId { get; set; }
    }
}
