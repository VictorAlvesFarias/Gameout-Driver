using Domain.Entitites.ApplicationContextDb;
using Web.Api.Toolkit.Identity.Domain.Entities;

namespace Domain.Entitites.Shared
{
    public class AppStoredFile : BaseUserOwnedEntity
    {
        public int AppFileId { get; set; }
        public AppFile AppFile { get; set; }
        public int? StoredFileId { get; set; }
        public StoredFile? StoredFile { get; set; }
        public bool Versioned { get; set; }
        public bool Processing { get; set; }
        public string Error { get; set; }
        public string Message { get; set; }

        public void Update(int? appFileId = null, int? storedFileId = null, bool? versioned = null, bool? processing = null, string error = null, string mensagem = null)
        {
            AppFileId = appFileId ?? AppFileId;
            StoredFileId = storedFileId ?? StoredFileId;
            Versioned = versioned ?? Versioned;
            Processing = processing ?? Processing;
            UpdateDate = DateTime.Now;
            Error = error ?? Error;
            Message = mensagem ?? Message;
        }
    }
}
