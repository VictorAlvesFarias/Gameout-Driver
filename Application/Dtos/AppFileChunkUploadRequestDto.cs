using Microsoft.AspNetCore.Http;

namespace Application.Dtos
{
    public class AppFileChunkUploadRequestDto
    {
        public int AppStoredFileId { get; set; }
        public int ChunkIndex { get; set; }
        public int TotalChunks { get; set; }
        public int TraceId { get; set; }
        public IFormFile ChunkData { get; set; }
    }
}
