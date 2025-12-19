SELECT 
    je.RecordingJobId,
    je.CorrelationId,
    je.TotalConversationFetched,
    je.SuccessCount,
    je.FailedCount,
    je.JobDurationSeconds,
    je.JobStartTime,
    je.JobEndTime,
    COUNT(DISTINCT rpd.RecordingProcessId) AS TotalRecordings,
    COUNT(DISTINCT jc.CompanyId) AS TotalCompanies
FROM cas_RecordingJobResult je
LEFT JOIN cas_RecordingProcessDetail rpd ON je.RecordingJobId = rpd.RecordingJobId
LEFT JOIN cas_RecordingJobCompany jc ON je.RecordingJobId = jc.RecordingJobId
GROUP BY 
    je.RecordingJobId,
    je.CorrelationId,
    je.TotalConversationFetched,
    je.SuccessCount,
    je.FailedCount,
    je.JobDurationSeconds,
    je.JobStartTime,
    je.JobEndTime;
