DECLARE @RecordingDetailsJson NVARCHAR(MAX) = N'[
    {
        "leadTransitId": 663335167,
        "isFileExist": false,
        "isFetchedFromCDR": true,
        "isConvertedToMp3Variants": true,
        "isMovedToContentServer": true,
        "isMovedToGCS": true,
        "signedUrl": "",
        "isRecordingAlreadyBeingProcessed": false
    },
    {
        "leadTransitId": 663335510,
        "isFileExist": false,
        "isFetchedFromCDR": true,
        "isConvertedToMp3Variants": true,
        "isMovedToContentServer": true,
        "isMovedToGCS": true,
        "signedUrl": "",
        "isRecordingAlreadyBeingProcessed": false
    }
]';

DECLARE @CompanyIdsJson NVARCHAR(MAX) = N'[1,2,3,4,5,6,7,8,9,10]';

EXEC usp_InsertRecordingJobResult
    @CorrelationId = '7a944a34-008a-4b97-b61b-2a6eee684a47',
    @TotalConversationFetched = 5,
    @SuccessCount = 3,
    @FailedCount = 2,
    @JobStartTime = '2025-12-19T20:46:16.6224547',
    @JobEndTime = '2025-12-19T20:46:43.0097435',
    @RecordingDetails = @RecordingDetailsJson,
    @CompanyIds = @CompanyIdsJson;

GO
