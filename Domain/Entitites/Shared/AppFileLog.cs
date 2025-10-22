using Packages.Identity.Domain.Entities;

namespace Domain.Entitites.Shared
{
    public class AppFileLog : BaseUserOwnedEntity
    {
        public int? AppFileId { get; set; }
        public int? AppStoredFileId { get; set; }
        public int? StoredFileId { get; set; }
        public string Path { get; set; }
        public string RecordName { get; set; }
        public string ActionMessage { get; set; }
        public int ActionType { get; set; }
    }
}
