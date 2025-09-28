using CASRecordingFetchJob.Helpers;
using CASRecordingFetchJob.Model;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Text.Json;
using System.Text;
using System.Diagnostics.Eventing.Reader;
using System.Collections.Concurrent;
using static Org.BouncyCastle.Math.EC.ECCurve;
using Google.Cloud.Storage.V1;

namespace CASRecordingFetchJob.Services
{
    public class RecordingDownloader
    {
        private readonly ILogger<RecordingDownloader> _logger;
        private readonly SshClientHelper _sshClientHelper;
        private readonly GoogleCloudStorageHelper _gcsHelper;
        private readonly CommonFunctions _commonFunctions;

        public RecordingDownloader(GoogleCloudStorageHelper gcsHelper, ILogger<RecordingDownloader> logger, SshClientHelper sshClientHelper, CommonFunctions commonFunctions)
        {
            _gcsHelper = gcsHelper;
            _logger = logger;
            _sshClientHelper = sshClientHelper;
            _commonFunctions = commonFunctions;
        }
        public async Task<Stream?> RestoreAndFetchCdrRecordingAsync(DateTime startTimeFrom, DateTime startTimeTo, string called, int leadtransitId = 0, int companyId = 0, bool isRestoreCdrRecording = false)
        {
            try
            {
                using var httpClient = new HttpClient();

                foreach (var number in _commonFunctions.GetCallableNumbers(called))
                {
                    var cdrUrl = _commonFunctions.GetCdrRecordingUrl(startTimeFrom, startTimeTo, number, null);
                    var response = await httpClient.GetAsync(cdrUrl);

                    if (!response.IsSuccessStatusCode)
                        continue;

                    var responseBody = await response.Content.ReadAsStringAsync();
                    var cdrResponse = JsonConvert.DeserializeObject<CdrResponse>(responseBody);

                    var cdr = cdrResponse?.Cdr?.LastOrDefault();
                    if (cdr == null)
                        continue;

                    var recordingUrl = _commonFunctions.GetCdrRecordingUrl(startTimeFrom, startTimeTo, number, cdr.CdrId);

                    return await DownloadWithRestoreAsync(recordingUrl, [cdr], isRestoreCdrRecording);
                }
            }
            catch (Exception ex)
            {
                return null;
            }
            return null;
        }

        public async Task<List<Cdr>?> TryFetchingCdrDetailsAsync(DateTime startTimeFrom, DateTime startTimeTo, string called)
        {
            var cdrBag = new ConcurrentBag<Cdr>();
            var numbers = _commonFunctions.GetCallableNumbers(called);
            using var httpClient = new HttpClient();

            await Parallel.ForEachAsync(numbers, async (number, token) =>
            {
                var url = _commonFunctions.GetCdrRecordingUrl(startTimeFrom, startTimeTo, number, null);
                var response = await httpClient.GetAsync(url, token);

                if (!response.IsSuccessStatusCode)
                    return;

                var json = await response.Content.ReadAsStringAsync(token);
                var cdrResponse = JsonConvert.DeserializeObject<CdrResponse>(json);

                var lastCdr = cdrResponse?.Cdr?.LastOrDefault();
                if (lastCdr != null)
                {
                    cdrBag.Add(lastCdr);
                }
            });

            if (cdrBag.IsEmpty) return null;

            return [.. cdrBag];
        }

        public async Task<List<Cdr>> FetchCdrDetails(List<Conversation> conversations)
        {
            var cdrPayloadData = _commonFunctions.GetCdrPayloadData(conversations);
            var cdrBag = new ConcurrentBag<Cdr>();

            await Parallel.ForEachAsync(cdrPayloadData, 
                new ParallelOptions { MaxDegreeOfParallelism = 10},
                async (payload, token) =>
            {
                var result = await TryFetchingCdrDetailsAsync( payload.StartTime, payload.EndTime, payload.Called);

                if (result != null && result.Count > 0)
                    foreach (var cdr in result)
                        cdrBag.Add(cdr);
            });
            return [.. cdrBag];
        }

        public async Task<Stream?> DownloadWithRestoreAsync(string recordingUrl, List<Cdr> cdr, bool restoreCdrRescording)
        {
            var stream = await TryDownloadAsync(recordingUrl);
            if (stream != null) return stream;

            if (!restoreCdrRescording) return null;

            var result = await RestoreCdrFilesOnVoipServerAsync(cdr);
            if (result) return await TryDownloadAsync(recordingUrl);

            return null;
        }

        public async Task<Stream?> TryDownloadAsync(string recordingUrl)
        {
            using var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(recordingUrl);

            if (!response.IsSuccessStatusCode) return null;

            var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? string.Empty;
            var bytes = await response.Content.ReadAsByteArrayAsync();

            return (contentType == "audio/x-wav" && bytes.Length > 0)
                ? new MemoryStream(bytes)
                : null;
        }

        public async Task<bool> RestoreAllCdrFilesOnVoipServerAsync(List<DateTime> dateList)
        {
            _logger.LogInformation("Execute script on VoIP server to download CDR files");

            return await _sshClientHelper.RunScriptAsync(dateList); ;
        }
        public async Task<bool> RestoreCdrFilesOnVoipServerAsync(List<Cdr> cdrDetails)
        {
            _logger.LogInformation("Execute script on VoIP server to download CDR files");
            var cdrFileDetails = cdrDetails.Select(cdr => new CdrFileDetails
            {
                RtpFile = $"{cdr.CallDate:yyyy-MM-dd/HH/mm}/RTP/rtp_{cdr.CallDate:yyyy-MM-dd-HH-mm}.tar",
                SipFile = $"{cdr.CallDate:yyyy-MM-dd/HH/mm}/SIP/sip_{cdr.CallDate:yyyy-MM-dd-HH-mm}.tar.gz",
                PcapFile = $"{cdr.CallId}.pcap"
            }).ToList();

            string parameter = string.Join(" ", cdrFileDetails.Select(a => $"{a.RtpFile} {a.PcapFile} {a.SipFile} {a.PcapFile}"));

            return await _sshClientHelper.RunRestoreRecordingPcapScriptAsync(parameter);
        }

        public async Task<string?> SaveStreamToFileAsync(Stream inputStream, string outputFilePath)
        {
            if (inputStream == null)
            {
                _logger.LogInformation("Input Stream is Empty");
                return null;
            }

            if (string.IsNullOrWhiteSpace(outputFilePath))
            {
                _logger.LogInformation("Output file path cannot be null or empty");
                return null;
            }

            if (inputStream.CanSeek)
                inputStream.Position = 0;

            Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath)!);

            using var fileStream = File.Create(outputFilePath);
            await inputStream.CopyToAsync(fileStream);
            return outputFilePath;
        }

        public async Task<string?> GetAnnouncementFile(string basePath, string pauseAnnouncementRelativePath)
        {
            var announcementPath = Path.Combine(basePath, pauseAnnouncementRelativePath);
            if(File.Exists(announcementPath))
                return announcementPath;

            Directory.CreateDirectory(Path.GetDirectoryName(announcementPath)!);
            var stream = await _gcsHelper.DownloadObject(pauseAnnouncementRelativePath.Replace("\\","/"));
            if (stream == null)
                return null;

            using (stream)
            using (var fileStream = File.Create(announcementPath))
            {
                await stream.CopyToAsync(fileStream);
            }
            return announcementPath;
        }

        public async Task<Stream?> FetchCdrRecordingAsync(DateTime startTimeFrom, DateTime startTimeTo, string called, int leadtransitId = 0, int companyId = 0, bool isRestoreCdrRecording = false)
        {
            try
            {
                foreach (var callableNumber in _commonFunctions.GetCallableNumbers(called))
                {
                    _logger.LogInformation($"[{companyId}] [{leadtransitId}] Fetch Recording from CDR, Callable Number {callableNumber}");
                    var cdrUrl = _commonFunctions.GetCdrRecordingUrl(startTimeFrom, startTimeTo, callableNumber, null);
                    _logger.LogInformation($"[{companyId}] [{leadtransitId}] CDR Url {cdrUrl}");
                    using (var httpClient = new HttpClient())
                    {
                        var response = await httpClient.GetAsync(cdrUrl);
                        if (!response.IsSuccessStatusCode)
                        {
                            _logger.LogInformation($"[{companyId}] [{leadtransitId}] CDR Response Status Code {response.StatusCode}");
                            return null;
                        }

                        var responseBody = await response.Content.ReadAsStringAsync();
                        var cdrResponse = JsonConvert.DeserializeObject<CdrResponse>(responseBody);

                        if (cdrResponse == null || cdrResponse.Cdr == null || cdrResponse.Cdr.Count == 0)
                        {
                            _logger.LogInformation($"[{companyId}] [{leadtransitId}] CDR Response Empty");
                            continue;
                        }

                        string cdrId = string.Empty;
                        var callDate = DateTime.MinValue;
                        foreach (var cdr in cdrResponse.Cdr)
                        {
                            cdrId = cdr.CdrId;
                            callDate = cdr.CallDate;
                        }
                        _logger.LogInformation($"[{companyId}] [{leadtransitId}] Found CDR, cdr id {cdrId}");

                        var recordingUrl = _commonFunctions.GetCdrRecordingUrl(startTimeFrom, startTimeTo, callableNumber, cdrId);

                        _logger.LogInformation($"[{companyId}] [{leadtransitId}] Download CDR recording url {recordingUrl}");

                        var cdrRecordingResponse = await httpClient.GetAsync(recordingUrl);

                        var contentType = cdrRecordingResponse.Content.Headers.ContentType?.MediaType ?? string.Empty;

                        var bytes = await cdrRecordingResponse.Content.ReadAsByteArrayAsync();

                        if (string.Equals(contentType, "audio/x-wav", StringComparison.OrdinalIgnoreCase) && bytes != null)
                        {
                            _logger.LogInformation($"[{companyId}] [{leadtransitId}] Downloaded CDR recording");
                            var memoryStream = new MemoryStream(bytes)
                            {
                                Position = 0
                            };
                            return memoryStream;
                        }
                        else if (string.Equals(contentType, "text/html", StringComparison.OrdinalIgnoreCase) && bytes != null)
                        {
                            _logger.LogInformation($"[{companyId}] [{leadtransitId}] Download CDR Recording Failed");
                            string responseText = Encoding.UTF8.GetString(bytes);

                            using var doc = JsonDocument.Parse(responseText);
                            if (doc.RootElement.TryGetProperty("errors", out var errors) &&
                                errors.TryGetProperty("reason", out var reason))
                            {
                                var error = reason.GetString();
                                _logger.LogInformation($"[{companyId}] [{leadtransitId}] Download Failed Reason {error}");
                            }

                            return null;
                        }
                        else
                        {
                            _logger.LogInformation($"[{companyId}] [{leadtransitId}] Something went wrong cdr recording failed to download, contentType {contentType}, is Downloaded Stream Empty {bytes == null}");
                            return null;
                        }
                    }
                }
                _logger.LogInformation($"[{companyId}] [{leadtransitId}] No valid callable number found, dialed number {called}");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{companyId}] [{leadtransitId}] No valid callable number found, dialed number {called}", ex);
                return null;
            }
        }
    }
}
