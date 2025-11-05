using Web.Api.Toolkit.Helpers.Domain.Interfaces;
using Web.Api.Toolkit.Identity.Domain.Entities;

namespace Domain.Entitites.ApplicationContextDb
{
    public class StoredFile : BaseUserOwnedEntity, IFileBase
    {
        public string Name { get; set; }
        public string MimeType { get; set; }
        public byte[] Base64 { get; set; }
        public double SizeInBytes { get; set; }

        public void Update(string _name, string _mimeType, byte[] _base64, double _sizeInBytes)
        {
            Name = _name ?? Name;
            MimeType = _mimeType ?? MimeType;
            Base64 = _base64 ?? Base64;
            UpdateDate = DateTime.Now;
            SizeInBytes = _sizeInBytes > 0 ? _sizeInBytes : SizeInBytes;
        }
        public byte[] GetBytes()
        {
            return Base64;
        }
        public string GetMimeType()
        {
            return MimeType;
        }
    }
}
