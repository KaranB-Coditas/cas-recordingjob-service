using Newtonsoft.Json;

namespace CASRecordingFetchJob.Model
{
    public class RestoreResult
    {
        public string? Date { get; set; }
        public string? Status { get; set; }
        public string? Reason { get; set; }
    }
    public class RestorePcapResult
    {
        [JsonProperty("tar")]
        public string? Tar { get; set; }
        [JsonProperty("status")]
        public string? Status { get; set; }
        [JsonProperty("reason")]
        public string? Reason { get; set; }
        [JsonProperty("file")]
        public string? File { get; set; }
    }
}
