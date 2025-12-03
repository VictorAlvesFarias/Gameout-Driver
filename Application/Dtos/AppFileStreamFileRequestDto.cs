using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Queues.AppFileDtos
{
    public class AppFileStreamFileRequestDto
    {
        public int AppStoredFileId { get; set; }
        public int OriginalFileSize { get; set; }
        public int TraceId { get; set; }
        public IFormFile File { get; set; }
    }
}
