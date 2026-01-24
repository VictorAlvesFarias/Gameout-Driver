namespace Application.Dtos
{
    public class AppFileChunkUploadResponseDto
    {
        public bool ChunkReceived { get; set; }
        public int ChunkIndex { get; set; }
        public bool IsComplete { get; set; }
        public string Message { get; set; }
    }
}
