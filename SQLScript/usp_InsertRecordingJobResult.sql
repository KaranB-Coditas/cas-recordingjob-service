CREATE PROCEDURE usp_InsertRecordingJobResult
    @CorrelationId UNIQUEIDENTIFIER,
    @TotalConversationFetched INT,
    @SuccessCount INT,
    @FailedCount INT,
    @JobStartTime DATETIME2,
    @JobEndTime DATETIME2,
    @RecordingDetails NVARCHAR(MAX), -- JSON array of recording details
    @CompanyIds NVARCHAR(MAX) -- JSON array of company IDs
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    
    BEGIN TRY
        DECLARE @RecordingJobId BIGINT;
        
        -- Insert main job execution
        INSERT INTO cas_RecordingJobResult (
            CorrelationId,
            TotalConversationFetched,
            SuccessCount,
            FailedCount,
            JobStartTime,
            JobEndTime
        )
        VALUES (
            @CorrelationId,
            @TotalConversationFetched,
            @SuccessCount,
            @FailedCount,
            @JobStartTime,
            @JobEndTime
        );
        
        SET @RecordingJobId = SCOPE_IDENTITY();
        
        -- Insert recording process details
        INSERT INTO cas_RecordingProcessDetail (
            RecordingJobId,
            LeadTransitId,
            IsFileExist,
            IsFetchedFromCDR,
            IsConvertedToMp3Variants,
            IsMovedToContentServer,
            IsMovedToGCS,
            SignedUrl,
            IsRecordingAlreadyBeingProcessed
        )
        SELECT 
            @RecordingJobId,
            LeadTransitId,
            IsFileExist,
            IsFetchedFromCDR,
            IsConvertedToMp3Variants,
            IsMovedToContentServer,
            IsMovedToGCS,
            NULLIF(SignedUrl, ''),
            IsRecordingAlreadyBeingProcessed
        FROM OPENJSON(@RecordingDetails)
        WITH (
            LeadTransitId BIGINT,
            IsFileExist BIT,
            IsFetchedFromCDR BIT,
            IsConvertedToMp3Variants BIT,
            IsMovedToContentServer BIT,
            IsMovedToGCS BIT,
            SignedUrl NVARCHAR(2000),
            IsRecordingAlreadyBeingProcessed BIT
        );
        
        -- Insert company IDs
        INSERT INTO cas_RecordingJobCompany (RecordingJobId, CompanyId)
        SELECT @RecordingJobId, value
        FROM OPENJSON(@CompanyIds);
        
        COMMIT TRANSACTION;
        
        SELECT @RecordingJobId AS RcecordingJobId;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        
        THROW;
    END CATCH
END
GO
