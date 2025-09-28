namespace CASRecordingFetchJob.Helpers
{
    public interface ICorrelationIdAccessor
    {
        string? CorrelationId { get; }
    }
    public class CorrelationIdAccessor : ICorrelationIdAccessor
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        public const string HeaderName = "X-Correlation-ID";

        public CorrelationIdAccessor(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public string? CorrelationId =>
            _httpContextAccessor.HttpContext?.Items[HeaderName]?.ToString()
            ?? _httpContextAccessor.HttpContext?.TraceIdentifier;
    }
}
