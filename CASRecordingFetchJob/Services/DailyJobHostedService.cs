using CASRecordingFetchJob.Helpers;
using CASRecordingFetchJob.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CASRecordingFetchJob.Services
{
    public class DailyJobHostedService : BackgroundService
    {
        private readonly ILogger<DailyJobHostedService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly int _scheduledHour = 23;
        private readonly int _scheduledMinute = 0;
        private readonly int _scheduledSecond = 0;
        private readonly DailyJobSettings _settings;

        public DailyJobHostedService(ILogger<DailyJobHostedService> logger, IServiceProvider serviceProvider, IOptions<DailyJobSettings> settings)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _settings = settings.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_settings.Enabled)
            {
                _logger.LogInformation("Recording Job Disabled, terminating recording job");
                return;
            }
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.Now;
                var scheduledTime = now.Date + _settings.RunAt;

                if (now > scheduledTime)
                    scheduledTime = scheduledTime.AddDays(1);

                var delay = scheduledTime - now;
                _logger.LogInformation("Next job scheduled at {Time}", scheduledTime);

                await Task.Delay(delay, stoppingToken);

                try
                {
                    await RunDailyJobAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while running daily job");
                }
            }
        }

        private async Task RunDailyJobAsync()
        {
            _logger.LogInformation("Daily job started at {Time}", DateTime.Now);

            using var scope = _serviceProvider.CreateScope();
            var myJobService = scope.ServiceProvider.GetRequiredService<IRecordingJobService>();
            await myJobService.ExecuteRecordingJobAsync(leadtransitId: 2074299831);

            _logger.LogInformation("Daily job completed at {Time}", DateTime.Now);
        }
    }
}
