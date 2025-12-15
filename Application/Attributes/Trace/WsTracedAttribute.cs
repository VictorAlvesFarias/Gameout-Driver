using Application.Services.LoggingService;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using System.Net.WebSockets;
using System.Text.Json;
using Web.Api.Toolkit.Helpers.Application.Dtos;
using Web.Api.Toolkit.Ws.Application.Attributes;
using Web.Api.Toolkit.Ws.Application.Contexts;
using Web.Api.Toolkit.Ws.Application.Dtos;

namespace Application.Attributes.Trace
{
    public class WsTracedAttribute : WsActionFilterAttribute
    {
        private readonly string HEADER_NAME = "X-Trace-Application-Id";

        public WsTracedAttribute() { }

        public bool OnCreate { get; set; }

        public override Task OnActionExecutingAsync(WebSocketRequestContext req)
        {    
            var logginService = (ILoggingService)req.Services.GetService(typeof(ILoggingService));
            
            try
            {
                var context = JsonSerializer.Deserialize<WebSocketRequest>(req.Request);

                if(!context.Headers.TryGetValue(HEADER_NAME, out var traceId))
                {
                    req.Error = true;
                    req.ErrorMessage = "Trace Id header is missing.";

                    logginService.SendErrorLogAsync(
                        $"Trace Id header is missing.",
                        "Error",
                        ""
                    );

                    return base.OnActionExecuted(req);
                }
            }
            catch (Exception ex)
            {
                logginService.SendErrorLogAsync(
                    $"Error in WsTracedAttribute",
                    "Error",
                    $"{ex.Message} : {ex.StackTrace}" ?? ""
                );

                req.Exception = ex;
                req.Error = true;
            }

            return base.OnActionExecuted(req);

        }
    }
}
