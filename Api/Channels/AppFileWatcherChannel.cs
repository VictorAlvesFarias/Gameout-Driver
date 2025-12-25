using Application.Attributes.Trace;
using Application.Services.AppFileWatcherService;
using Application.Workers;
using Domain.Queues.AppFileDtos;
using System.Threading.Tasks;
using Web.Api.Toolkit.Ws.Application.Attributes;
using Web.Api.Toolkit.Ws.Application.Channels;
using Web.Api.Toolkit.Ws.Application.Contexts;

namespace Application.Channels
{
    public class AppFileWatcherChannel : WebSocketChannelBase<DriveWebSocketClientWorker>
    {
        private readonly AppFileWatcherService _appFileWatcherService;

        public AppFileWatcherChannel(AppFileWatcherService appFileWatcherService)
        {
            _appFileWatcherService = appFileWatcherService;
        }

        [WsTraced]
        [WsAction("SetEvents")]
        public void SetEvents(WebSocketRequestContext context)
        {
            _appFileWatcherService.SetWatchers();
        }

        [WsTraced]
        [WsAction("SingleSync")]
        public void SingleSync(AppFileUpdateRequestMessage context)
        {
            _appFileWatcherService.SingleSync(context);
        }

        [WsTraced]
        [WsAction("CheckStatus")]
        public async Task CheckStatus(AppFileStatusCheckRequestMessage context)
        {
            await _appFileWatcherService.IsProcessing(context);
        }

        [WsTraced]
        [WsAction("CheckAppFileStatusAll")]
        public async Task CheckAppFileStatusAll(AppFileStatusCheckAllRequestMessage context)
        {
            await _appFileWatcherService.CheckAppFileStatusAll(context);
        }
    }
}


