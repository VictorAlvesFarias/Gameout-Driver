using Packages.Identity.Domain.Entities;

namespace Domain.Entitites.ApplicationContextDb
{
    public class AppFile : BaseUserOwnedEntity
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public bool VersionControl { get; set; }
        public bool Observer { get; set; }
        public bool AutoValidateSync { get; set; }
        public bool Synced { get; set; }

        public void Update(string? name = null, string? path = null, bool? versionControl = null, bool? observer = null, bool? autoValidateSync = null, bool? synced = null)
        {
            Name = name ?? Name;
            Path = path ?? Path;
            VersionControl = versionControl ?? VersionControl;
            Observer = observer ?? Observer;
            UpdateDate = DateTime.Now;
            Synced = synced ?? Synced;
            AutoValidateSync = autoValidateSync ?? AutoValidateSync;
        }
    }
}
