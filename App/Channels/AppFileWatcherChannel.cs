using Application.Attributes.Trace;
using Application.Services.AppFileWatcherService;
using App.Workers;
using Domain.Queues.AppFileDtos;
using System.Threading.Tasks;
using Web.Api.Toolkit.Ws.Application.Attributes;
using Web.Api.Toolkit.Ws.Application.Channels;
using Web.Api.Toolkit.Ws.Application.Contexts;

namespace App.Channels
{
    public class AppFileWatcherChannel : WebSocketChannelBase<DriveWebSocketClientWorker>
    {
        private readonly IAppFileService _appFileService;

        public AppFileWatcherChannel(IAppFileService appFileService)
        {
            _appFileService = appFileService;
        }

        [WsTraced]
        [WsAction("SetEvents")]
        public void SetEvents(WebSocketRequestContext context)
        {
            try
            {
                _appFileService.SetWatchers();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
        }

        [WsTraced]
        [WsAction("SingleSync")]
        public void SingleSync(AppFileUpdateRequestMessage context)
        {
            try
            {
                _appFileService.SingleSync(context);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
        }

        [WsTraced]
        [WsAction("CheckStatus")]
        public async Task CheckStatus(AppFileStatusCheckRequestMessage context)
        {
            try
            {
                await _appFileService.IsProcessing(context);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
        }

        [WsTraced]
        [WsAction("CheckAppFileStatusAll")]
        public async Task CheckAppFileStatusAll(AppFileStatusCheckAllRequestMessage context)
        {
            try
            {
                await _appFileService.CheckAppFileStatusAll(context);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
        }
    }
}
