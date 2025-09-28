namespace CASRecordingFetchJob.Model
{
    public class RecordingJobResponse
    {
        public string CorrelationId { get; set; } = string.Empty;
        public int TotalConversationFetched { get; set; }
        public List<RecordingDetails> RecordingProcessDetails { get; set; } = [];
        public int SuccessfulCount { get; set; }
        public int FailedCount { get; set; }
        public DateTime JobStartTime { get; set; }
        public DateTime JobEndTime { get; set; }
        public List<int> CompanyIdsToProcess { get; set; } = [];
    }
    public class RecordingDetails
    {
        public int LeadTransitId { get; set; }
        public bool IsFileExist { get; set; }
        public bool IsFetchedFromCDR { get; set; }
        public bool IsConvertedToMp3Variants { get; set; }
        public bool IsMovedToContentServer { get; set; }
        public bool IsMovedToGCS { get; set; }
        public string SignedUrl { get; set; } = string.Empty;
        public bool IsRecordingAlreadybeingProcessed { get; set; }
    }
}