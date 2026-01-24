using Microsoft.AspNetCore.Http;

namespace Application.Dtos
{
    public class AppFileChunkUploadRequestDto
    {
        public int AppFileId { get; set; }
        public int ChunkIndex { get; set; }
        public int TotalChunks { get; set; }
        public string UploadId { get; set; }
        public int OriginalFileSize { get; set; }
        public int TraceId { get; set; }
        public IFormFile ChunkData { get; set; }
    }
}
