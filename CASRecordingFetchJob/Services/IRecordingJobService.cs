using CASRecordingFetchJob.Model;
using Microsoft.AspNetCore.Mvc;

namespace CASRecordingFetchJob.Services
{
    public interface IRecordingJobService
    {
        Task<IActionResult> ExecuteRecordingJob(DateTime? startDate = null, DateTime? endDate = null, int companyId = 0, int leadtransitId = 0, bool isRestoreCdrRecordingEnabled = false, bool addPauseAnnouncement = false, bool isDualConsent = false, bool generateSignedUrl = false);
        Task<string?> GenerateSignedUrl(int leadtransitId, bool isDualConsent, DateTime? conversationDate = null, bool? isUserControlledRecording = null);
    }
}
