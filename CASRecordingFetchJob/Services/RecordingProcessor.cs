using CASRecordingFetchJob.Helpers;
using FFMpegCore.Pipes;
using FFMpegCore;
using System.CodeDom;
using FFMpegCore.Enums;
using CASRecordingFetchJob.Model;
using Org.BouncyCastle.Utilities.Zlib;
using static Org.BouncyCastle.Math.Primes;
using System.IO;
using Google.Type;

namespace CASRecordingFetchJob.Services
{
    public class RecordingProcessor
    {
        private readonly ILogger<RecordingProcessor> _logger;
        private readonly RecordingDownloader _recordingDownloader;
        public RecordingProcessor(ILogger<RecordingProcessor> logger, RecordingDownloader recordingDownloader)
        {
            _logger = logger;
            _recordingDownloader = recordingDownloader;
        }
        public async Task<Dictionary<string, Stream>> ConvertWavToMp3VariantsAsync(
            Stream? wavStream, 
            int leadtransitId,
            bool trimAgentPart, 
            int seekTime, 
            bool saveWavFile, 
            bool isDualConsentRecording, 
            List<RecordInterval> recordIntervals,
            int companyId,
            string recordingBasePath,
            bool addPauseAnnouncement
            )
        {
            var convertedRecordings = new Dictionary<string, Stream>();
            try
            {
                if (wavStream == null || wavStream.Length == 0)
                    throw new InvalidOperationException("Input stream is empty.");
                _logger.LogInformation($"[{companyId}] [{leadtransitId}] Started Converting Recordins to Mp3");

                byte[] wavBytes;
                using (var ms = new MemoryStream())
                {
                    await wavStream.CopyToAsync(ms);
                    wavBytes = ms.ToArray();
                }
                wavStream.Dispose();

                var task = new List<Task<KeyValuePair<string, Stream>>>()
                {
                    ConvertToMp3(new MemoryStream(wavBytes), leadtransitId, companyId)
                };

                if (isDualConsentRecording)
                    task.Add(RemoveProspectChannelRecording(new MemoryStream(wavBytes), leadtransitId, companyId));

                if (trimAgentPart && seekTime > 0)
                    task.Add(TrimAgentRecording(new MemoryStream(wavBytes), leadtransitId, companyId, seekTime));

                if (recordIntervals != null && recordIntervals.Count > 0)
                    task.Add(ClipRecording(new MemoryStream(wavBytes), leadtransitId, companyId, recordIntervals, recordingBasePath, addPauseAnnouncement));

                var result = await Task.WhenAll(task);

                foreach (var kvp in result)
                    convertedRecordings[kvp.Key] = kvp.Value;

                if (saveWavFile)
                    convertedRecordings[$"{leadtransitId}.wav"] = new MemoryStream(wavBytes);

                _logger.LogInformation($"[{companyId}] [{leadtransitId}] Ended Converting Recordins to Mp3");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[{companyId}] [{leadtransitId}] Error = Convert Recordins To Mp3 Failed");
            }
            return convertedRecordings;

        }

        public async Task<KeyValuePair<string, Stream>> ConvertToMp3(Stream wavStream, int leadTransitId, int companyId)
        {
            var normalStream = await ConvertWithFfmpegUsingStreamInputAsync(
                wavStream, companyId, leadTransitId,
                options => { });

            return new KeyValuePair<string, Stream>($"{leadTransitId}.mp3", normalStream);
        }

        public async Task<KeyValuePair<string, Stream>> RemoveProspectChannelRecording(Stream wavStream, int leadTransitId, int companyId)
        {
            var monoStream = await ConvertWithFfmpegUsingStreamInputAsync(
                wavStream, companyId, leadTransitId,
                options => options.WithCustomArgument("-af \"pan=mono|c0=c0\""));

            return new KeyValuePair<string, Stream>($"{leadTransitId}_pitcher.mp3", monoStream);
        }

        public async Task<KeyValuePair<string, Stream>> TrimAgentRecording(Stream wavStream, int leadTransitId, int companyId, int seekTime)
        {
            var trimmedStream = await ConvertWithFfmpegUsingStreamInputAsync(
                wavStream, companyId, leadTransitId,
                options => options.WithCustomArgument($"-ss {seekTime}"));

            return new KeyValuePair<string, Stream>($"{leadTransitId}_original.mp3", trimmedStream);
        }
        public async Task<KeyValuePair<string, Stream>> ClipRecording(Stream wavStream, int leadTransitId, int companyId, List<RecordInterval> recordingInterval, string recordingBasePath, bool addPauseAnnouncement)
        {
            if (recordingInterval == null || recordingInterval.Count == 0)
                return new KeyValuePair<string, Stream>();

            const string pauseAnnouncementRelativePath = @"RecordingAnnouncement\_RecordingStopped.mp3";
            recordingBasePath = $"{recordingBasePath}\\{leadTransitId}_temp";

            string? announcementFile = null;

            var clippedFiles = new List<string>();

            var wavFilePath = await _recordingDownloader.SaveStreamToFileAsync(wavStream, $"{recordingBasePath}\\{leadTransitId}.wav") 
                ?? throw new InvalidOperationException("Failed to save WAV stream to file.");

            if (addPauseAnnouncement)
                announcementFile = await _recordingDownloader.GetAnnouncementFile(recordingBasePath, pauseAnnouncementRelativePath);

            foreach (var interval in recordingInterval)
            {

                var tempFilePath = Path.Combine(recordingBasePath, $"{leadTransitId}_{interval.RecordStartTime}_{interval.RecordStopTime}.mp3");
                Directory.CreateDirectory(Path.GetDirectoryName(tempFilePath)!);

                var isClipped = await ConvertWithFfmpegUsingFileInputAsync(
                    wavFilePath, 
                    tempFilePath,
                    companyId, 
                    leadTransitId,
                    options => options.WithCustomArgument($"-ss {interval.RecordStartTime} -to {interval.RecordStopTime}")
                    );

                if (isClipped)
                {
                    clippedFiles.Add(tempFilePath);

                    if (announcementFile != null)
                        clippedFiles.Add(announcementFile);
                }
            }
            var finalFilePath = Path.Combine(recordingBasePath, $"{leadTransitId}_clipped.mp3");
            Directory.CreateDirectory(Path.GetDirectoryName(finalFilePath)!);

            var result = await ConcatWithFfmpegUsingFileInputAsync(clippedFiles, finalFilePath, companyId, leadTransitId, options => {});

            var memoryStream = new MemoryStream();
            using (var fileStream = File.OpenRead(finalFilePath))
                await fileStream.CopyToAsync(memoryStream);

            memoryStream.Position = 0;

            try
            {
                Directory.Delete(recordingBasePath, recursive: true);
            }
            catch (Exception ex)
            {
            }

            return new KeyValuePair<string, Stream>($"{leadTransitId}_userControlledConsent.mp3", memoryStream);
        }
        private async Task<Stream> ConvertWithFfmpegUsingStreamInputAsync(Stream inputStream, int companyId, int leadTransitId, Action<FFMpegArgumentOptions> configureOptions)
        {
            if (inputStream.CanSeek)
                inputStream.Position = 0;

            var outputStream = new MemoryStream();

            try
            {
                await FFMpegArguments
                    .FromPipeInput(new StreamPipeSource(inputStream))
                    .OutputToPipe(new StreamPipeSink(outputStream), options =>
                    {
                        configureOptions(options);
                        options.WithAudioCodec(AudioCodec.LibMp3Lame)
                               .ForceFormat("mp3");
                    })
                    .ProcessAsynchronously();

                outputStream.Position = 0;
                return outputStream;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{companyId}] [{leadTransitId}] FFmpeg conversion failed", ex);
                outputStream.Dispose();
                throw;
            }
        }

        public async Task<bool> ConvertWithFfmpegUsingFileInputAsync(string inputFile, string outputFile, int companyId, int leadTransitId, Action<FFMpegArgumentOptions> configureOptions)
        {
            try
            {
                await FFMpegArguments
                    .FromFileInput(inputFile)
                    .OutputToFile(outputFile, true, options =>
                    {
                        configureOptions(options);
                        options.WithAudioCodec(AudioCodec.LibMp3Lame)
                               .ForceFormat("mp3");
                    })
                    .ProcessAsynchronously();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{companyId}] [{leadTransitId}] FFmpeg conversion failed", ex);
                return false;
            }
        }
        public async Task<bool> ConcatWithFfmpegUsingFileInputAsync(List<string> inputFileList, string outputFile, int companyId, int leadTransitId, Action<FFMpegArgumentOptions> configureOptions)
        {
            try
            {
                await FFMpegArguments
                    .FromConcatInput(inputFileList)
                    .OutputToFile(outputFile, true, options =>
                    {
                        configureOptions(options);
                        options.WithAudioCodec(AudioCodec.LibMp3Lame)
                               .ForceFormat("mp3");
                    })
                    .ProcessAsynchronously();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{companyId}] [{leadTransitId}] FFmpeg conversion failed", ex);
                return false;
            }
        }
    }
}
