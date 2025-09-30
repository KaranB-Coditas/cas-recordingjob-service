using CASRecordingFetchJob.Model;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;
using Newtonsoft.Json;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.ConstrainedExecution;
using FFMpegCore;
using FFMpegCore.Pipes;
using FFMpegCore.Enums;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using System.Text;
using System.ComponentModel.Design;
using CASRecordingFetchJob.Helpers;
using System.Security.AccessControl;
using Serilog.Core;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Polly.CircuitBreaker;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Security.Cryptography.Xml;
using StackExchange.Redis;
using static CASRecordingFetchJob.Helpers.CommonFunctions;

namespace CASRecordingFetchJob.Services
{
    public class RecordingJobService : IRecordingJobService
    {
        private readonly IConfiguration _config;
        private readonly RecordingJobDBContext _db;
        private readonly GoogleCloudStorageHelper _gcsHelper;
        private readonly ILogger<RecordingJobService> _logger;
        private readonly bool _trimAgentPart;
        private readonly SshClientHelper _sshClientHelper;
        private readonly bool _cdrRestoreOnJob;
        private readonly int _maxDegreeOfParallelism;
        private readonly int _cdrDataRetentionDays;
        private readonly RecordingDataService _recordingDataService;
        private readonly RecordingProcessor _recordingProcessor;
        private readonly RecordingDownloader _recordingDownloader;
        private readonly RecordingMover _recordingMover;
        private readonly CommonFunctions _commonFunctions;
        private readonly bool _saveWavRecording;
        private readonly ICorrelationIdAccessor _correlationIdAccessor;
        private readonly bool _processDualConsentRecording;
        private readonly int _signedUrlExpiredTimeHours;
        private readonly IDistributedLockManager _lockManager;

        public RecordingJobService(
            IConfiguration config, 
            RecordingJobDBContext db, 
            GoogleCloudStorageHelper gcsHelper, 
            ILogger<RecordingJobService> logger,
            SshClientHelper sshClientHelper,
            RecordingDataService recordingDataService,
            RecordingProcessor recordingProcessor,
            RecordingDownloader recordingDownloader,
            CommonFunctions commonFunctions,
            RecordingMover recordingMover,
            ICorrelationIdAccessor correlationIdAccessor,
            IDistributedLockManager lockManager
            ) 
        {
            _config = config;
            _db = db;
            _gcsHelper = gcsHelper;
            _logger = logger;
            _trimAgentPart = config.GetValue<bool?>("TrimAgentPart") ?? false;
            _sshClientHelper = sshClientHelper;
            _cdrRestoreOnJob = config.GetValue<bool?>("CdrRestoreOnJob") ?? false;
            _maxDegreeOfParallelism = config.GetValue<int?>("MaxDegreeOfParallelism") ?? 1;
            _cdrDataRetentionDays = _config.GetValue<int?>("CdrDataRetentionDays") ?? 6;
            _recordingDataService = recordingDataService;
            _recordingProcessor = recordingProcessor;
            _recordingDownloader = recordingDownloader;
            _commonFunctions = commonFunctions;
            _recordingMover = recordingMover;
            _saveWavRecording = _config.GetValue<bool?>("SaveWAVRecording") ?? false;
            _processDualConsentRecording = _config.GetValue<bool?>("ProcessDualConsentRecording") ?? false;
            _correlationIdAccessor = correlationIdAccessor;
            _signedUrlExpiredTimeHours = _config.GetValue<int?>("SignedUrlExpiredTimeHours") ?? 168;
            _lockManager = lockManager;
        }
        
        public async Task<IActionResult> ExecuteRecordingJobAsync(
            DateTime? startDate = null, 
            DateTime? endDate = null, 
            int companyId = 0, 
            int leadtransitId = 0,
            bool isRestoreCdrRecordingEnabled = false,
            bool addPauseAnnouncement = false,
            bool isDualConsent = false,
            bool generateSignedUrl = false
            )
        {
            if(leadtransitId < 0)
            {
                _logger.LogInformation("LeadtransitId must be greater than 0");
                return new BadRequestObjectResult(new { error = "Invalid Leadtransit Id" });
            }

            startDate ??= DateTime.Now.AddDays(-1);
            endDate ??= startDate;

            if (endDate < startDate)
            {
                _logger.LogInformation("End date must be greater than or equal to start date");
                return new BadRequestObjectResult(new { error = "End date must be greater than or equal to start date" });
            }

            isRestoreCdrRecordingEnabled = isRestoreCdrRecordingEnabled || _cdrRestoreOnJob;
            var isRestoreCdrRecordingRequired = startDate < DateTime.Now.AddDays(-_cdrDataRetentionDays);
            if (!isRestoreCdrRecordingEnabled && isRestoreCdrRecordingRequired && leadtransitId == 0)
            {
                _logger.LogInformation("Date must be within the last 6 days. CDR recording may not be available for processing.");
                return new BadRequestObjectResult(new { error = "Date must be within the last 6 days. CDR recording may not be available for processing." });
            }

            var recordingJobResponse = new RecordingJobResponse
            {
                CorrelationId = _correlationIdAccessor.CorrelationId ?? Guid.NewGuid().ToString(),
                JobStartTime = DateTime.Now
            };

            _logger.LogInformation("Recording Job Started");

            var companyIdsToProcess = await _recordingDataService.GetCompanyIdsAsync(companyId, leadtransitId);
            if (companyIdsToProcess == null || companyIdsToProcess.Count == 0)
            {
                _logger.LogInformation("No Company present for recording job");
                return new BadRequestObjectResult(new { error = "No Company Ids present for recording job" });
            }
            recordingJobResponse.CompanyIdsToProcess = companyIdsToProcess;
            _logger.LogInformation($"Company Ids to Process for Job {string.Join(", ", companyIdsToProcess)}");

            var callDetails = await _recordingDataService.GetCallDetailsAsync(
                startDate.Value, 
                endDate.Value, 
                companyIdsToProcess, 
                leadtransitId);

            if (callDetails == null || callDetails.Count == 0)
            {
                _logger.LogInformation($"No Conversation Record Found");
                return new BadRequestObjectResult(new { error = "No conversation record found" });
            }

            var agentInitiatedConversationPhoneCalls = new Dictionary<int, DateTime>();
            if (_trimAgentPart)
                agentInitiatedConversationPhoneCalls = await _recordingDataService.GetAgentInitiatedConversationPhoneCalls(callDetails, companyIdsToProcess);

            var conversationIds = callDetails
                .Where(a => a.CallType == 2)
                .Select(a => a.LeadtransitId)
                .Distinct()
                .ToList();

            if (conversationIds.Count == 0)
            {
                _logger.LogInformation($"No matching calls");
                return new BadRequestObjectResult(new { error = "No matching calls" });
            }
            recordingJobResponse.TotalConversationFetched = conversationIds.Count;
            _logger.LogInformation($"Conversation Count {conversationIds.Count}");

            //cdr recording restoration
            if(isRestoreCdrRecordingEnabled && isRestoreCdrRecordingRequired)
            {
                var cdrDetails = await _recordingDownloader.FetchCdrDetails(callDetails);
                await _recordingDownloader.RestoreCdrFilesOnVoipServerAsync(cdrDetails);
            }

            var processDetails = new ConcurrentBag<RecordingDetails>();
            var successfulIds = new ConcurrentBag<int>();
            var failedIds = new ConcurrentBag<int>();

            await Parallel.ForEachAsync(conversationIds,
                new ParallelOptions { MaxDegreeOfParallelism = _maxDegreeOfParallelism },
                async (id, cancellationToken) =>
                {
                    var recordingJobInfo = new RecordingDetails { LeadTransitId = id };

                    var lockHandle = await _lockManager.AcquireLockAsync($"lock:{id}", TimeSpan.FromMinutes(5));
                    _logger.LogInformation($"Locked leadtransitid {id} for processing");

                    try
                    {
                        var agentTrimTime = 0;
                        var conversation = callDetails.Where(a => a.LeadtransitId == id).ToList();
                        var conversationDate = _commonFunctions.ConvertTimeZoneFromUtcToPST(conversation[0].LeadCatchTime);
                        var clientId = conversation[0].ClientId;
                        var recordCall = conversation[0].RecordCall;

                        if (lockHandle == null)
                        {
                            _logger.LogInformation($"[{clientId}] [{leadtransitId}] Recording {id} is already being processed.");
                            recordingJobInfo.IsRecordingAlreadybeingProcessed = true;
                            return;
                        }

                        if (!recordCall)
                        {
                            _logger.LogInformation($"[{clientId}] [{id}] User contolled recording, call not recorded");
                            failedIds.Add(id);
                            return;
                        }

                        if (_trimAgentPart && conversation.Count > 1)
                            agentTrimTime = _recordingDataService.GetAgentTrimTime(conversation, agentInitiatedConversationPhoneCalls ?? []);

                        var recordingIntervals = _commonFunctions.GetRecordingIntervals(conversation[0].RecordingInterval, agentTrimTime);

                        string recordingsBasePath = _commonFunctions.GetRecordingPath(conversationDate);

                        string gcsRecordingPath = _commonFunctions.GetGcsRecordingPath(conversationDate);

                        _logger.LogInformation($"[{clientId}] [{id}] Started Processing Recording for LeadtransitId {id}, Conversation on {conversationDate} PST, Agent Trim Time {agentTrimTime} sec");

                        Directory.CreateDirectory(recordingsBasePath);

                        var isUserControlledRecording = (recordingIntervals != null && recordingIntervals.Count > 0);
                        
                        RecordingType recordingType = isDualConsent 
                        ? RecordingType.DualConsent : isUserControlledRecording ? RecordingType.UserControlled : RecordingType.Normal;

                        var audioFormat = _commonFunctions.GetSupportedAudioFormat();
                        var audioFile = recordingType switch
                        {
                            RecordingType.DualConsent => $"{id}{DualConsent}{audioFormat}",
                            RecordingType.UserControlled => $"{id}{UserControlled}{audioFormat}",
                            _ => id + audioFormat
                        };

                        string audioFileName = Path.Combine(recordingsBasePath, audioFile);
                        if (File.Exists(audioFileName))
                        {
                            recordingJobInfo.IsFileExist = true;
                            _logger.LogInformation($"[{clientId}] [{id}] File already exist - {id}{_commonFunctions.GetSupportedAudioFormat()}");
                            successfulIds.Add(id);
                            return;
                        }

                        var cdrRecording = await _recordingDownloader.RestoreAndFetchCdrRecordingAsync(
                            conversationDate.AddMinutes(-1),
                            conversationDate.AddMinutes(2),
                            _commonFunctions.GetDialedNumber(
                                conversation[0].PrimaryNumberIndex,
                                conversation[0].ContactTel1,
                                conversation[0].ContactTel2,
                                conversation[0].ContactTel3,
                                conversation[0].BestPhoneNumber),
                            id,
                            clientId,
                            isRestoreCdrRecordingEnabled);

                        if (cdrRecording == null)
                        {
                            _logger.LogInformation($"[{clientId}] [{id}] CDR recording not found");
                            failedIds.Add(id);
                            return;
                        }
                        recordingJobInfo.IsFetchedFromCDR = true;

                        var convertedRecordings = await _recordingProcessor.ConvertWavToMp3VariantsAsync(
                            cdrRecording,
                            id,
                            _trimAgentPart,
                            agentTrimTime,
                            _saveWavRecording,
                            isDualConsent || _processDualConsentRecording,
                            recordingIntervals ?? [],
                            clientId,
                            recordingsBasePath,
                            addPauseAnnouncement);

                        if (convertedRecordings == null || convertedRecordings.Count == 0)
                        {
                            _logger.LogInformation($"[{clientId}] [{id}] Recording failed to convert");
                            failedIds.Add(id);
                            return;
                        }

                        if (!_commonFunctions.TryValidateRecording(convertedRecordings, audioFile, id))
                        {
                            _logger.LogInformation($"[{clientId}] [{id}] Recording variant {recordingType.ToString()} failed to convert");
                            failedIds.Add(id);
                            return;
                        }

                        recordingJobInfo.IsConvertedToMp3Variants = true;

                        var MovingToContentServerResult = await _recordingMover.MoveToContentServerAsync(convertedRecordings, recordingsBasePath, id, clientId);
                        recordingJobInfo.IsMovedToContentServer = MovingToContentServerResult;

                        var MovingToGCSResult = await _recordingMover.MoveToGcsAsync(convertedRecordings, gcsRecordingPath, id, clientId);
                        recordingJobInfo.IsMovedToGCS = MovingToGCSResult;

                        if (!MovingToContentServerResult || !MovingToGCSResult)
                        {
                            _logger.LogInformation($"[{clientId}] [{id}] Recording failed to move to content server or GCS");
                            failedIds.Add(id);
                            return;
                        }
                        if (generateSignedUrl)
                            recordingJobInfo.SignedUrl = await GenerateSignedUrl(id, isDualConsent, recordingIntervals != null && recordingIntervals.Count > 0, gcsRecordingPath, clientId);

                        successfulIds.Add(id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error processing {id}");
                        failedIds.Add(id);
                    }
                    finally
                    {
                        processDetails.Add(recordingJobInfo);
                        lockHandle?.Dispose();
                        _logger.LogInformation($"Lock released for leadtransitid {id}");
                    }
                });

            recordingJobResponse.SuccessfulCount = successfulIds.Count;
            recordingJobResponse.FailedCount = failedIds.Count;
            recordingJobResponse.RecordingProcessDetails = processDetails.ToList();
            recordingJobResponse.JobEndTime = DateTime.Now;

            _logger.LogInformation($"SuccessfulIds Count {successfulIds.Count}, FailedIds Count {failedIds.Count}");
            _logger.LogInformation($"FailedIds LeadtransitId {string.Join(", ", failedIds)}");

            _logger.LogInformation("Recording Job Ended");
            return new OkObjectResult(recordingJobResponse);
        }

        public async Task<string> GenerateSignedUrl(int leadtransitId, bool isDualConsent, bool isUserControlledRecording, string gcsRecordingPath, int companyId = 0)
        {
            var objectName = string.Join("/", gcsRecordingPath, leadtransitId + "_original.mp3");
            if (isDualConsent)
                objectName = string.Join("/", gcsRecordingPath, leadtransitId + "_pitcher.mp3");
            else if (isUserControlledRecording)
                objectName = string.Join("/", gcsRecordingPath, leadtransitId + "_userControlledConsent.mp3");
            
            var signedUrl = string.Empty;
            try
            {
                signedUrl = await _gcsHelper.SignUrlAsync(objectName, TimeSpan.FromHours(_signedUrlExpiredTimeHours));
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogError(ex,$"[{companyId}] [{leadtransitId}] Signed Url generation failed for {objectName}. Retry with objectName {leadtransitId}.mp3");
                signedUrl = await _gcsHelper.SignUrlAsync($"{leadtransitId}.mp3", TimeSpan.FromHours(_signedUrlExpiredTimeHours));   
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"[{companyId}] [{leadtransitId}] Signed Url generation failed for {objectName}");
            }
            return signedUrl;
        }

        public async Task<string?> GenerateSignedUrlAsync(int leadtransitId, bool isDualConsent, DateTime? conversationOn = null, bool? isUserControlledRecording = null)
        {
            if (leadtransitId <= 0)
            {
                _logger.LogInformation("LeadtransitId is required");
                return null;
            }

            string gcsRecordingPath = string.Empty;
            var conversationDate = conversationOn ?? DateTime.MinValue;
            if (conversationDate == DateTime.MinValue || isUserControlledRecording == null)
            {
                var conversation = await _recordingDataService.GetCallDetailsByLeadtransitIdAsync(leadtransitId);
                if (conversation == null || conversation.Count == 0)
                {
                    _logger.LogInformation($"[{leadtransitId}] No Conversation Record Found");
                    return null;
                }

                if (!conversation[0].RecordCall)
                {
                    _logger.LogInformation($"[{leadtransitId}] User contolled recording, call not recorded");
                    return null;
                }
                conversationDate = conversation[0].LeadCatchTime;

                var recordingIntervals = _commonFunctions.GetRecordingIntervals(conversation[0].RecordingInterval, 0);
                isUserControlledRecording = recordingIntervals != null && recordingIntervals.Count > 0;
            }

            var conversationDatePst = _commonFunctions.ConvertTimeZoneFromUtcToPST(conversationDate);

            gcsRecordingPath = _commonFunctions.GetGcsRecordingPath(conversationDatePst);

            return await GenerateSignedUrl(leadtransitId, isDualConsent, isUserControlledRecording ?? false, gcsRecordingPath);
        }

        public async Task<bool> RestoreCdrRecordingByLeadtransitIdAsync(int leadtransitId)
        {
            var callDetails = await _recordingDataService.GetCallDetailsByLeadtransitIdAsync(leadtransitId);
            var cdrDetails = await _recordingDownloader.FetchCdrDetails(callDetails);
            return await _recordingDownloader.RestoreCdrFilesOnVoipServerAsync(cdrDetails);
        }
    }
}
