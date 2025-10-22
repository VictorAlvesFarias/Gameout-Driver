using Domain.Entitites.ApplicationContextDb;

namespace Domain.Queues.AppFileDtos
{
    public class AppFileSetEventsRequestMessage
    {
        public List<AppFile> AppFiles { get; set; }
    }
}
