using Application.Types;

namespace Application.Dtos.AppFile
{
    public class UpdateAppStoredFileStatusRequestDto
    {
        public int AppStoredFileId { get; set; }
        public AppStoredFileStatusTypes Status { get; set; }
        public int TraceId { get; set; }
    }
}

