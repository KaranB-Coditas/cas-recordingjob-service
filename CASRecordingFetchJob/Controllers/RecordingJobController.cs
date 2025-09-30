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

        [HttpPost("ExecuteAsync")]
        public async Task<IActionResult> ExecuteAsync([FromBody] ExecuteJobPayload request)
        {
            return await _recordingJobService.ExecuteRecordingJobAsync(
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

        [HttpPost("GenerateSignedUrlAsync")]
        public async Task<IActionResult> GenerateSignedUrlAsync([FromBody] SignedUrlPayload signedUrlPayload)
        {
            var result = await _recordingJobService.GenerateSignedUrlAsync(
                signedUrlPayload.LeadtransitId, 
                signedUrlPayload.IsDualConsent, 
                signedUrlPayload.ConversationDate, 
                signedUrlPayload.IsUserControlledRecording
                );

            if (string.IsNullOrEmpty(result))
                return NotFound("Recording not found or unable to generate signed URL.");

            return new OkObjectResult(new {signedUrl= result});
        }

        [HttpPost("RestoreCdrRecordingByLeadtransitIdAsync")]
        public async Task<IActionResult> RestoreCdrRecordingByLeadtransitIdAsync([FromQuery] int leadtransitId)
        {
            if (leadtransitId <= 0)
                return BadRequest("LeadTransitId is not valid"); 
            return new OkObjectResult(await _recordingJobService.RestoreCdrRecordingByLeadtransitIdAsync(leadtransitId));
        }

        [HttpPost("RestoreCDRRecordingsByDateAsync")]
        public async Task<IActionResult> RestoreCDRRecordingsByDateAsync([FromQuery] string rawDateInput)
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
