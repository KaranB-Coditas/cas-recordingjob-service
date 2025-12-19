CREATE TABLE cas_RecordingJobResult (
    RecordingJobId BIGINT IDENTITY(1,1) PRIMARY KEY,
    CorrelationId UNIQUEIDENTIFIER NOT NULL,
    TotalConversationFetched INT NOT NULL,
    SuccessCount INT NOT NULL,
    FailedCount INT NOT NULL,
    JobStartTime DATETIME2 NOT NULL,
    JobEndTime DATETIME2 NOT NULL,
    JobDurationSeconds AS DATEDIFF(SECOND, JobStartTime, JobEndTime) PERSISTED,
    CreatedAt DATETIME2 DEFAULT GETDATE(),
    INDEX IX_CorrelationId (CorrelationId),
    INDEX IX_JobStartTime (JobStartTime)
);

CREATE TABLE cas_RecordingProcessDetail (
    RecordingProcessId BIGINT IDENTITY(1,1) PRIMARY KEY,
    RecordingJobId BIGINT NOT NULL,
    LeadTransitId BIGINT NOT NULL,
    IsFileExist BIT NOT NULL,
    IsFetchedFromCDR BIT NOT NULL,
    IsConvertedToMp3Variants BIT NOT NULL,
    IsMovedToContentServer BIT NOT NULL,
    IsMovedToGCS BIT NOT NULL,
    SignedUrl NVARCHAR(2000) NULL,
    IsRecordingAlreadyBeingProcessed BIT NOT NULL,
    ProcessingStatus AS (
        CASE 
            WHEN IsFetchedFromCDR = 1 AND IsConvertedToMp3Variants = 1 
                 AND IsMovedToContentServer = 1 AND IsMovedToGCS = 1 
            THEN 'Success'
            ELSE 'Failed'
        END
    ) PERSISTED,
    CreatedAt DATETIME2 DEFAULT GETDATE(),
    FOREIGN KEY (RecordingJobId) REFERENCES cas_RecordingJobResult(RecordingJobId),
    INDEX IX_LeadTransitId (LeadTransitId),
    INDEX IX_RecordingJobId (RecordingJobId),
    INDEX IX_ProcessingStatus (ProcessingStatus)
);

CREATE TABLE cas_RecordingJobCompany (
    RecordingJobCompanyId BIGINT IDENTITY(1,1) PRIMARY KEY,
    RecordingJobId BIGINT NOT NULL,
    CompanyId INT NOT NULL,
    CreatedAt DATETIME2 DEFAULT GETDATE(),
    FOREIGN KEY (RecordingJobId) REFERENCES cas_RecordingJobResult(RecordingJobId),
    INDEX IX_RecordingJobId_CompanyId (RecordingJobId, CompanyId)
);
