using Application.Types;

namespace Application.Dtos.AppFile
{
    public class UpdateAppFileStatusRequestDto
    {
        public int AppFileId { get; set; }
        public AppFileStatusTypes Status { get; set; }
        public string StatusMessage { get; set; }
        public string StatusDetails { get; set; }
        public int TraceId { get; set; }
    }
}
