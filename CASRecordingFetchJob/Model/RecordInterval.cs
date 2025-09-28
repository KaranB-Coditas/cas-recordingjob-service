using System.Text.Json.Serialization;

namespace CASRecordingFetchJob.Model
{
    public class RecordInterval
    {
        [JsonPropertyName("RecordStartTime")]
        public int RecordStartTime { get; set; }

        [JsonPropertyName("RecordStopTime")]
        public int RecordStopTime { get; set; }
    }
}
