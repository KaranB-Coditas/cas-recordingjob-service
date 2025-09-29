namespace CASRecordingFetchJob.Model
{
    public class ExecuteJobPayload
    {
        public DateTime? StartDate { get; set; } = null;
        public DateTime? EndDate { get; set; } = null;
        public int CompanyId { get; set; } = 0;
        public int LeadtransitId { get; set; } = 0;
        public bool IsRestoreCdrRecordingEnabled { get; set; } = false;
        public bool AddPauseAnnouncement { get; set; } = false;
        public bool IsDualConsent { get; set; } = false;
        public bool GenerateSignedUrl { get; set; } = false;
    }
}
