using Application.Types;

namespace Application.Dtos
{
    public class AppStoredFileUpdateStatusRequestDto
    {
        public int AppStoredFileId { get; set; }
        public AppStoredFileStatusTypes Status { get; set; }
    }
}
