using CASRecordingFetchJob.Helpers;
using static Org.BouncyCastle.Math.EC.ECCurve;

namespace CASRecordingFetchJob.Services
{
    public class RecordingMover
    {
        private readonly ILogger<RecordingMover> _logger;
        private readonly IConfiguration _config;
        private readonly GoogleCloudStorageHelper _gcsHelper;
        public RecordingMover(ILogger<RecordingMover> logger, IConfiguration config, GoogleCloudStorageHelper gcsHelper)
        {
            _logger = logger;
            _config = config;
            _gcsHelper = gcsHelper;
        }

        public async Task<bool> MoveToContentServerAsync(Dictionary<string, Stream> recordings, string recordingBasePath, int leadtransitId, int companyId)
        {
            try
            {
                _logger.LogInformation($"[{companyId}] [{leadtransitId}] Copy Recordings to Content Server");

                var results = await Task.WhenAll(
                    recordings.Select(r => MoveRecordingToContentServerAsync(r, recordingBasePath, leadtransitId, companyId))
                    );

                return results.All(r => r);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{companyId}] [{leadtransitId}] Recordings Copied to Content Server Failed", ex);
                return false;
            }
        }
        public async Task<bool> MoveRecordingToContentServerAsync(KeyValuePair<string, Stream> recording, string recordingBasePath, int leadtransitId, int companyId)
        {
            try
            {
                var filePath = Path.Combine(recordingBasePath, recording.Key);

                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);
                await recording.Value.CopyToAsync(fileStream);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{companyId}] [{leadtransitId}] Recordings {recording.Key} Copied to Content Server Failed", ex);
                return false;
            }
        }

        public async Task<bool> MoveToGcsAsync(Dictionary<string, Stream> recordings, string gcsRecordingPath, int leadtransitId, int companyId)
        {
            try
            {
                _logger.LogInformation($"[{companyId}] [{leadtransitId}] Copy Recordings to GCS");

                var results = await Task.WhenAll(
                    recordings.Select(r => MoveRecordingToGCSAsync(r, gcsRecordingPath, leadtransitId, companyId))
                    );

                return results.All(r => r);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{companyId}] [{leadtransitId}] Recordings Copied to GCS Failed", ex);
                return false;
            }

        }

        public async Task<bool> MoveRecordingToGCSAsync(KeyValuePair<string, Stream> recording, string gcsRecordingPath, int leadtransitId, int companyId)
        {
            try
            {
                var key = $"{gcsRecordingPath}/{recording.Key}";
                var contentType = GetSupportedAudioFormat() == ".mp3" ? "audio/mpeg" : "audio/wav";
                await _gcsHelper.UploadRecordingAsync(key, recording.Value, contentType);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{companyId}] [{leadtransitId}] Recordings {recording.Key} Copied to GCS Failed", ex);
                return false;
            }

        }

        public string GetSupportedAudioFormat()
        {
            var format = _config.GetValue<string>("SupportedAudioFormat") ?? ".mp3";
            if (!format.StartsWith('.'))
                format = "." + format;
            return format;
        }

        public async Task MovingToGCSAndContentServer(Dictionary<string, Stream> recordings, string recordingBasePath, string gcsRecordingPath, int leadtransitId, int companyId)
        {
            try
            {
                _logger.LogInformation($"[{companyId}] [{leadtransitId}] Copy Recordings to GCS And Content Server");

                var tasks = new List<Task<bool>>();

                foreach (var recording in recordings)
                {
                    tasks.Add(MoveRecordingToContentServerAsync(recording, recordingBasePath, leadtransitId, companyId));
                    tasks.Add(MoveRecordingToGCSAsync(recording, gcsRecordingPath, leadtransitId, companyId));
                }

                var result = await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{companyId}] [{leadtransitId}] MovingToGCSAndContentServer Failed", ex);
            }

        }
    }
}
