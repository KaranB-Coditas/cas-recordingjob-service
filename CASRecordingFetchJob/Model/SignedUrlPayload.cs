using Microsoft.AspNetCore.Mvc;

namespace CASRecordingFetchJob.Model
{
    public class SignedUrlPayload
    {
        public int LeadtransitId { get; set; }
        public bool IsDualConsent { get; set; }
        public DateTime? ConversationDate { get; set; } = null;
        public bool? IsUserControlledRecording { get; set; } = null;

    }
}
