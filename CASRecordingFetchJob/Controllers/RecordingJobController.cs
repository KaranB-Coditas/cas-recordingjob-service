using CASRecordingFetchJob.Model;
using CASRecordingFetchJob.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace CASRecordingFetchJob.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RecordingJobController : ControllerBase
    {
        private readonly IRecordingJobService _recordingJobService;
        private readonly RecordingDownloader _recordingDownloader;
        
        public RecordingJobController(IRecordingJobService recordingJobService, RecordingDownloader recordingDownloader)
        {
            _recordingJobService = recordingJobService;
            _recordingDownloader = recordingDownloader;
        }

        [HttpPost("Execute")]
        public async Task<IActionResult> Execute([FromBody] ExecuteJobPayload request)
        {
            return await _recordingJobService.ExecuteRecordingJob(
                request.StartDate, 
                request.EndDate, 
                request.CompanyId, 
                request.LeadtransitId, 
                request.IsRestoreCdrRecordingEnabled, 
                request.AddPauseAnnouncement, 
                request.IsDualConsent, 
                request.GenerateSignedUrl
                );
        }

        [HttpPost("GenerateSignedUrl")]
        public async Task<IActionResult> GenerateSignedUrl([FromBody] SignedUrlPayload signedUrlPayload)
        {
            var result = await _recordingJobService.GenerateSignedUrl(
                signedUrlPayload.LeadtransitId, 
                signedUrlPayload.IsDualConsent, 
                signedUrlPayload.ConversationDate, 
                signedUrlPayload.IsUserControlledRecording
                );

            if (string.IsNullOrEmpty(result))
                return NotFound("Recording not found or unable to generate signed URL.");

            return new OkObjectResult(new {signedUrl= result});
        }

        [HttpPost("RestoreCdrRecordingOnVoipServer")]
        public async Task<IActionResult> RestoreUsingScriptAsync([FromBody] List<Cdr> request)
        {
            return new OkObjectResult(await _recordingDownloader.RestoreCdrFilesOnVoipServerAsync(request));
        }

        [HttpGet("RestoreCdrRecordingOnVoipServerByDate")]
        public async Task<IActionResult> RestoreCDRRecordings(string rawDateInput)
        {
            if (string.IsNullOrEmpty(rawDateInput))
                return BadRequest("Date parameter is required.");

            var dateList = new List<DateTime>();
            var seen = new HashSet<DateTime>();

            var dateString = rawDateInput.Split([',', '\n'], StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in dateString)
            {
                string trimmed = part.Trim().Trim('"');

                if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate) && seen.Add(parsedDate))
                    dateList.Add(parsedDate);
            }

            if (dateList.Count == 0)
                return BadRequest("Valid Date parameter is required");

            return new OkObjectResult(await _recordingDownloader.RestoreAllCdrFilesOnVoipServerAsync(dateList));
        }
    }
}
