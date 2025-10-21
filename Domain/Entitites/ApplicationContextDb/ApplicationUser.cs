using Packages.Identity.Domain.Entities;

namespace Domain.Entitites.ApplicationContextDb
{
    public class ApplicationUser : BaseEntityIdentity
    {
        public string Name { get; set; }
    }
}
