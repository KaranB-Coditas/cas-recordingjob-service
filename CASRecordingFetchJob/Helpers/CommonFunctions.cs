using CASRecordingFetchJob.Model;
using Newtonsoft.Json;
using System.Runtime.CompilerServices;
using static Org.BouncyCastle.Math.EC.ECCurve;

namespace CASRecordingFetchJob.Helpers
{
    public class CommonFunctions(IConfiguration config)
    {
        private readonly IConfiguration _config = config;

        public bool CheckRecordJobEnabled()
        {
            return _config.GetValue<bool?>("RecordingJobEnabled") ?? true;
        }
        public string GetRecordingsBasePath()
        {
            return _config.GetValue<string>("RecordingsBasePath") ?? string.Empty;
        }
        public string GetS3BucketName()
        {
            return _config.GetValue<string>("S3RecordingBaseKey") ?? string.Empty;
        }
        public string GetSupportedAudioFormat()
        {
            var format = _config.GetValue<string>("SupportedAudioFormat") ?? ".mp3";
            if (!format.StartsWith('.'))
                format = "." + format;
            return format;
        }

        public string GetCdrRecordingsServerBasePath()
        {
            return _config.GetValue<string>("RecordingsServerBasePath") ?? string.Empty;
        }

        public Dictionary<string, string> GetCdrRecordingServerCredentials()
        {
            var credentials = new Dictionary<string, string>();
            credentials.Add("CdrUserName", _config.GetValue<string>("CdrUserName") ?? string.Empty);
            credentials.Add("CdrPassword", _config.GetValue<string>("CdrPassword") ?? string.Empty);
            return credentials;
        }

        public List<string> GetCallableNumbers(string called)
        {
            List<string> callableNumbers = [];

            called = called.Replace("+", "").Trim();

            if (called.FirstOrDefault() != '1')
            {
                callableNumbers.Add("011" + called);
                callableNumbers.Add("1" + called);
            }
            else
                callableNumbers.Add(called);

            return callableNumbers;
        }
        public string GetCdrRecordingUrl(DateTime startTimeFrom, DateTime startTimeTo, string called, string? cdrId)
        {
            var cdrRecordingsServerBasePath = GetCdrRecordingsServerBasePath();
            var cdrRecordingServerCredentials = GetCdrRecordingServerCredentials();

            if (!string.IsNullOrEmpty(cdrId))
                return cdrRecordingsServerBasePath + "api.php?task=getVoiceRecording&user=" + cdrRecordingServerCredentials["CdrUserName"] + "&password=" + cdrRecordingServerCredentials["CdrPassword"] + "&params={\"cdrId\":\"" + cdrId + "\"}";
            else
                return cdrRecordingsServerBasePath + "api.php?task=getVoipCalls&user=" + cdrRecordingServerCredentials["CdrUserName"] + "&password=" + cdrRecordingServerCredentials["CdrPassword"] + "&params={\"startTime\":\"" + startTimeFrom + "\",\"startTimeTo\":\"" + startTimeTo + "\",\"called\":\"" + called + "\"}";
        }
        public DateTime ConvertTimeZoneFromUtcToPST(DateTime utcDateTime)
        {
            TimeZoneInfo PacificZone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
            var pstDateTime = TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, PacificZone);
            return pstDateTime;
        }
        public string GetDialedNumber(int dialingIndex, string phone1, string phone2, string phone3, string phone4)
        {
            var dialedNumber = string.Empty;
            switch (dialingIndex)
            {
                case 0:
                    dialedNumber = phone1;
                    break;
                case 1:
                    dialedNumber = phone2;
                    break;
                case 4:
                    dialedNumber = phone3;
                    break;
                case 5:
                    dialedNumber = phone4;
                    break;
                default:
                    dialedNumber = phone1;
                    break;
            }
            return dialedNumber;
        }
        public List<CdrPayloadData> GetCdrPayloadData(List<Conversation> conversations)
        {
            var uniqueConversations = conversations.Where(a => a.CallType == 1).GroupBy(a => a.LeadtransitId).Select(g => g.First()).ToList();

            return uniqueConversations.Select(a => new CdrPayloadData
            {
                StartTime = a.LeadCatchTime.AddMinutes(-1),
                EndTime = a.LeadCatchTime.AddMinutes(2),
                Called = GetDialedNumber(a.PrimaryNumberIndex, a.ContactTel1, a.ContactTel2, a.ContactTel3, a.BestPhoneNumber)
            }).ToList();
        }
        public List<RecordInterval> GetRecordingIntervals(string recordingIntervals, int agentTrimTime)
        {
            try
            {
                var recordingIntervalsList = string.IsNullOrWhiteSpace(recordingIntervals) || recordingIntervals == "[]"
                ? new List<RecordInterval>()
                : JsonConvert.DeserializeObject<List<RecordInterval>>(recordingIntervals) ?? new List<RecordInterval>();

                var validIntervals = recordingIntervalsList
                    .Where(interval =>
                        interval.RecordStartTime >= 0 &&
                        interval.RecordStopTime > interval.RecordStartTime)
                    .ToList();

                if (!validIntervals.Any())
                    return new List<RecordInterval>();
                var distinctIntervals = validIntervals
                    .GroupBy(interval => new { interval.RecordStartTime, interval.RecordStopTime })
                    .Select(group => group.First())
                    .ToList();

                foreach (var interval in distinctIntervals)
                {
                    interval.RecordStartTime += agentTrimTime;
                    interval.RecordStopTime += agentTrimTime;
                }

                return distinctIntervals;
            }
            catch (Exception)
            {
                return new List<RecordInterval>();
            }
        }
        public string GetRecordingPath(DateTime conversationDate)
        {
            return Path.Combine(
                GetRecordingsBasePath(),
                conversationDate.Year.ToString(),
                conversationDate.Month.ToString(),
                conversationDate.Day.ToString()
                );
        }
        public string GetGcsRecordingPath(DateTime conversationDate)
        {
            return string.Join("/",
                GetS3BucketName(),
                conversationDate.Year.ToString(),
                conversationDate.Month.ToString(),
                conversationDate.Day.ToString()
                );
        }

    }
}
