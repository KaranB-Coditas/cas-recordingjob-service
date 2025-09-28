
using Serilog.Context;

namespace CASRecordingFetchJob.Middleware
{
    public class CorrelationIdMiddleware(RequestDelegate next)
    {
        private readonly RequestDelegate _next = next;
        private const string CorrelationHeader = "X-Correlation-ID";

        public async Task InvokeAsync(HttpContext context)
        {
            var correlationId = context.Request.Headers[CorrelationHeader].FirstOrDefault()
                ?? Guid.NewGuid().ToString();

            context.Response.Headers[CorrelationHeader] = correlationId;
            context.TraceIdentifier = correlationId;
            context.Items[CorrelationHeader] = correlationId;

            using (LogContext.PushProperty("CorrelationId", correlationId))
            {
                await _next(context);
            }
        }
    }
}
