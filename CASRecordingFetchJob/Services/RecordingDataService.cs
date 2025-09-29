using CASRecordingFetchJob.Helpers;
using CASRecordingFetchJob.Model;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.Design;

namespace CASRecordingFetchJob.Services
{
    public class RecordingDataService
    {
        private readonly RecordingJobDBContext _db;
        private readonly ILogger<RecordingDataService> _logger;
        public RecordingDataService(RecordingJobDBContext db, ILogger<RecordingDataService> logger)
        {
            _db = db;
            _logger = logger;
        }
        public async Task<int> GetSeekTimeInSecondsAsync(int leadtransitId)
        {
            try
            {
                return await _db.GetAgentCallTransferedTimeDifferenceAsync(leadtransitId) ?? 0;
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Error in method {nameof(GetSeekTimeInSecondsAsync)}");
                return 0;
            }
        }
        public int GetAgentTrimTime(List<Conversation> conversation, Dictionary<int, DateTime> agentInitiatedConversationPhoneCalls)
        {
            var agentInitiatedConversationCallPlacedTime =
                agentInitiatedConversationPhoneCalls.TryGetValue(conversation[0].LeadtransitId, out DateTime value) ? value : DateTime.MinValue;

            var agentConversation = conversation
                .Where(a => a.CallType == 1)
                .FirstOrDefault();

            return agentConversation == null
                ? 0
                : (agentInitiatedConversationCallPlacedTime != DateTime.MinValue)
                    ? (int)(agentInitiatedConversationCallPlacedTime - agentConversation.LeadCatchTime).TotalSeconds
                    : (int)(agentConversation.CallSendTime - agentConversation.LeadCatchTime).TotalSeconds;
        }
        public async Task<Dictionary<int, DateTime>> GetPhoneCalls(List<int> leadtransitIds)
        {
            if (leadtransitIds == null || leadtransitIds.Count == 0)
                return [];
            try
            {
                return await _db.t_PhoneCall
                    .Where(pc => leadtransitIds.Contains(pc.LeadTransitId))
                    .OrderByDescending(pc => pc.CallPlacedTime)
                    .GroupBy(pc => pc.LeadTransitId)
                    .ToDictionaryAsync(
                        g => g.Key,
                        g => g.Max(x => x.CallPlacedTime) ?? DateTime.Now
                    );
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Error in method {nameof(GetPhoneCalls)}");
                return [];
            }
        }
        public async Task<Dictionary<int, DateTime>> GetAgentInitiatedConversationPhoneCalls(List<Conversation> conversations, List<int> companyIds)
        {
            if (conversations == null || companyIds == null)
                return [];

            var enabledCompanyIds = await GetAgentInitiatedCallEnabledCompanies(companyIds);
            if (enabledCompanyIds == null || enabledCompanyIds.Count == 0)
                return [];

            var agentInitiatedCallleadTransitIds = conversations
                .Where(a => enabledCompanyIds.Contains(a.ClientId))
                .Select(a => a.LeadtransitId)
                .Distinct()
                .ToList();

            if (agentInitiatedCallleadTransitIds == null || agentInitiatedCallleadTransitIds.Count == 0)
                return [];

            return await GetPhoneCalls(agentInitiatedCallleadTransitIds);
        }
        public async Task<List<Conversation>> GetCallDetailsAsync(DateTime startDate, DateTime endDate, List<int> clientIds, int leadtransitId = 0)
        {
            if (leadtransitId > 0)
                return await GetCallDetailsByLeadtransitIdAsync(leadtransitId);

            try
            {
                var query = from note in _db.cas_Note
                            join call in _db.t_Call
                                on note.LeadTransitId equals call.LeadTransitId
                            where note.CreateDate > startDate
                                  && note.CreateDate < endDate.AddDays(1)
                            orderby call.ClientId, call.LeadCatchTime
                            select new Conversation
                            {
                                LeadtransitId = note.LeadTransitId ?? 0,
                                LeadCatchTime = call.LeadCatchTime ?? DateTime.MinValue,
                                CallSendTime = call.CallSendTime ?? DateTime.MinValue,
                                PrimaryNumberIndex = call.PrimaryNumberIndex ?? 0,
                                ContactTel1 = call.ContactTel1 ?? string.Empty,
                                ContactTel2 = call.ContactTel2 ?? string.Empty,
                                ContactTel3 = call.ContactTel3 ?? string.Empty,
                                BestPhoneNumber = call.BestPhoneNumber ?? string.Empty,
                                CallType = call.CallType ?? 0,
                                TalkTime = call.TalkTime ?? 0,
                                ClientId = call.ClientId ?? 0,
                                RecordCall = note.RecordCall,
                                RecordingInterval = note.RecordingInterval ?? string.Empty
                            };

                if (clientIds != null)
                    query = query.Where(x => clientIds.Contains(x.ClientId));

                return await query.ToListAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Error in method {nameof(GetCallDetailsAsync)}.");
                return [];
            }

        }
        public async Task<List<Conversation>> GetCallDetailsByLeadtransitIdAsync(int leadtransitId)
        {
            try
            {
                var query = from note in _db.cas_Note
                            join call in _db.t_Call
                                on note.LeadTransitId equals call.LeadTransitId
                            where note.LeadTransitId == leadtransitId
                            select new Conversation
                            {
                                LeadtransitId = note.LeadTransitId ?? 0,
                                LeadCatchTime = call.LeadCatchTime ?? DateTime.MinValue,
                                CallSendTime = call.CallSendTime ?? DateTime.MinValue,
                                PrimaryNumberIndex = call.PrimaryNumberIndex ?? 0,
                                ContactTel1 = call.ContactTel1 ?? string.Empty,
                                ContactTel2 = call.ContactTel2 ?? string.Empty,
                                ContactTel3 = call.ContactTel3 ?? string.Empty,
                                BestPhoneNumber = call.BestPhoneNumber ?? string.Empty,
                                CallType = call.CallType ?? 0,
                                TalkTime = call.TalkTime ?? 0,
                                ClientId = call.ClientId ?? 0,
                                RecordCall = note.RecordCall,
                                RecordingInterval = note.RecordingInterval ?? string.Empty
                            };
                return await query.ToListAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Error in method {nameof(GetCallDetailsByLeadtransitIdAsync)}");
                return [];
            }
        }
        public async Task<List<int>> GetAgentInitiatedCallEnabledCompanies(List<int> allCompany)
        {
            try
            {
                return await _db.cas_CompanySetting
                .Where(a => a.SettingKey == "DisableAutomation" && a.SettingValue == Boolean.TrueString && allCompany.Contains(a.CompanyId))
                .Select(a => a.CompanyId)
                .ToListAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Error in method {nameof(GetAgentInitiatedCallEnabledCompanies)}");
                return [];
            }
            
        }
        public async Task<int> GetCompanyIdByLeadTransitId(int leadtransitId)
        {
            try
            {
                var companyId = await _db.t_Call
                    .Where(a => a.LeadTransitId == leadtransitId)
                    .Select(a => a.ClientId)
                    .FirstOrDefaultAsync();
                return companyId ?? 0;
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Error in method {nameof(GetCompanyIdByLeadTransitId)}");
                return 0;
            }
        }
        public async Task<List<int>> GetCompanyIdsAsync(int companyId = 0, int leadtransitId = 0)
        {
            _logger.LogInformation("Fetching company ids to process recording job");
            if (companyId > 0)
                return [companyId];
            if (leadtransitId > 0)
                return [await GetCompanyIdByLeadTransitId(leadtransitId)];

            var activeCompanyIds = new List<int>();
            try
            {
                activeCompanyIds = await _db.t_Company
                .Where(a => !a.IsDeleted)
                .Select(a => a.ID)
                .ToListAsync();

                var disableRecordingDownloadFromJobCompanyIds = await _db.cas_CompanySetting
                    .Where(a => a.SettingKey == "DisableRecordingDownloadFromJob" && a.SettingValue == Boolean.TrueString)
                    .Select(a => a.CompanyId)
                    .ToListAsync();

                _logger.LogInformation($"DisableRecordingDownloadFromJob CompanyIds {string.Join(", ", disableRecordingDownloadFromJobCompanyIds)} ");

                var realTimeRecordingEnabledCompanyIds = await _db.cas_CompanySetting
                    .Where(a => a.SettingKey == "EnableRealTimeRecording" && a.SettingValue == Boolean.TrueString)
                    .Select(a => a.CompanyId)
                    .ToListAsync();

                _logger.LogInformation($"EnableRealTimeRecording CompanyIds {string.Join(", ", realTimeRecordingEnabledCompanyIds)} ");

                var removeCompanyIds = disableRecordingDownloadFromJobCompanyIds.Union(realTimeRecordingEnabledCompanyIds).ToList();

                _logger.LogInformation($"Removed Company Ids from Job {string.Join(", ", disableRecordingDownloadFromJobCompanyIds)}");

                return activeCompanyIds.Except(removeCompanyIds).ToList();
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Error in method {nameof(GetCompanyIdsAsync)}");

                return activeCompanyIds;
            }

        }
    }
}
