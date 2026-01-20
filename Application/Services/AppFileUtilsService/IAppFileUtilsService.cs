using Application.Types;
using Domain.Queues.AppFileDtos;

namespace Application.Services.AppFileWatcherService
{
    public interface IAppFileUtilsService
    {
        Task SendAppFileStatus(int id, AppFileStatusTypes status, string traceId);
    }
}
