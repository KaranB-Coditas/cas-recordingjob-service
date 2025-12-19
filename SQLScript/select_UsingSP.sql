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

	-- Get failed recordings
SELECT 
    je.CorrelationId,
    rpd.LeadTransitId,
    rpd.IsFileExist,
    rpd.IsFetchedFromCDR,
    rpd.IsConvertedToMp3Variants,
    rpd.IsMovedToContentServer,
    rpd.IsMovedToGCS,
    rpd.ProcessingStatus
FROM cas_RecordingJobResult je
INNER JOIN cas_RecordingProcessDetail rpd ON je.RecordingJobId = rpd.RecordingJobId
WHERE rpd.ProcessingStatus = 'Failed';

-- Get job statistics by date
SELECT 
    CAST(JobStartTime AS DATE) AS JobDate,
    COUNT(*) AS TotalJobs,
    SUM(SuccessCount) AS TotalSuccessful,
    SUM(FailedCount) AS TotalFailed,
    AVG(JobDurationSeconds) AS AvgDurationSeconds
FROM cas_RecordingJobResult
GROUP BY CAST(JobStartTime AS DATE)
ORDER BY JobDate DESC;
