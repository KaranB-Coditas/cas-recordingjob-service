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
        [HttpGet("Execute")]
        public async Task<IActionResult> Execute(
            [FromQuery] DateTime? startDate = null, 
            [FromQuery] DateTime? endDate = null, 
            [FromQuery] int companyId = 0, 
            [FromQuery] int leadtransitId = 0, 
            [FromQuery] bool restoreCdrRecording = false, 
            [FromQuery] bool addPauseAnnouncement = false, 
            [FromQuery] bool isDualConsent = false, 
            [FromQuery] bool generateSignedUrl = false)
        {
            return await _recordingJobService.ExecuteRecordingJob(startDate, endDate, companyId, leadtransitId, restoreCdrRecording, addPauseAnnouncement, isDualConsent, generateSignedUrl);
        }

        [HttpGet("RestoreAllCdrRecordingOnVoipServer")]
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
        [HttpPost("RestoreCdrRecordingOnVoipServer")]
        public async Task<IActionResult> RestoreUsingScriptAsync([FromBody] List<Cdr> request)
        {
            return new OkObjectResult(await _recordingDownloader.RestoreCdrFilesOnVoipServerAsync(request));
        }
        [HttpGet("GenerateSignedUrl")]
        public async Task<IActionResult> GenerateSignedUrl([FromQuery] int leadtransitId, [FromQuery] bool isDualConsent, [FromQuery] DateTime? conversationDate = null, [FromQuery] bool? isUserControlledRecording = null)
        {
            var result = await _recordingJobService.GenerateSignedUrl(leadtransitId, isDualConsent, conversationDate, isUserControlledRecording);
            if (string.IsNullOrEmpty(result))
                return NotFound("Recording not found or unable to generate signed URL.");
            return new OkObjectResult( new {signedUrl= result});
        }

    }
}
