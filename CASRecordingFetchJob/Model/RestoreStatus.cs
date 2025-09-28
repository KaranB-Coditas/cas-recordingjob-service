namespace CASRecordingFetchJob.Model
{
    public class RestoreStatus
    {
        public string Date { get; set; } = string.Empty;
        public string Status { get; set; } = "In-Progress"; 
        public string? Reason { get; set; } = null;
    }

}
