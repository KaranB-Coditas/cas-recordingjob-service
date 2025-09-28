namespace CASRecordingFetchJob.Model
{
    public class CdrPayloadData
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string Called { get; set; } = string.Empty;
    }
    public class CdrFileDetails
    {
        public string SipFile { get; set; }
        public string RtpFile { get; set; }
        public string PcapFile { get; set; }
    }
}
