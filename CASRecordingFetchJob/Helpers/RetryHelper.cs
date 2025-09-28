
using Polly;
using Polly.Retry;

namespace CASRecordingFetchJob.Helpers
{
    public static class RetryHelper
    {
        public static AsyncRetryPolicy<T> CreateRetryPolicy<T>( int maxRetries, TimeSpan delay, ILogger<T>? logger = null)
        {
            return Policy
                .HandleResult<T>(r => r == null) 
                .Or<Exception>()
                .WaitAndRetryAsync(
                    maxRetries,
                    attempt => delay,
                    (outcome, timespan, attempt, context) =>
                    {
                        logger?.LogWarning(
                            $"Retry {attempt}/{maxRetries} after {timespan.TotalSeconds}s due to {outcome.Exception?.Message ?? "null result"}");
                    });
        }

        public static async Task<T?> ExecuteWithRetryAsync<T>(Func<Task<T>> action, int maxRetries, TimeSpan delay, ILogger<T>? logger = null)
        {
            var policy = CreateRetryPolicy<T>(maxRetries, delay, logger);
            return await policy.ExecuteAsync(action);
        }

    }
}
