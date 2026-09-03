CAS Recording Fetch Job
ASP.NET Core 8 Web API that fetches ConnectAndSell call recordings from the CDR (Call Detail Record) system, converts them to MP3 variants, and stores them on the content server and Google Cloud Storage (GCS).

What it does
Loads eligible companies and conversations from SQL Server (skips companies with DisableRecordingDownloadFromJob or EnableRealTimeRecording).
Fetches matching WAV recordings from the CDR API by dialed number and call time window.
Converts each recording with FFmpeg into three MP3 variants:
{leadTransitId}.mp3 — full stereo
{leadTransitId}_pitcher.mp3 — agent/mono channel only
{leadTransitId}_trimmed.mp3 — trimmed using agent transfer timing
Writes files to the local content server path and uploads the same objects to GCS.
Returns a summary of successes, failures, and per-recording process details.
Tech stack
Area	Technology
Runtime	.NET 8
API	ASP.NET Core Web API + Swagger
Data	SQL Server, Entity Framework Core
Audio	FFMpegCore / FFmpeg
Storage	Local content server path, Google Cloud Storage
Logging	Serilog (console + rolling file)
Prerequisites
.NET 8 SDK
SQL Server access to the ConnectAndSell database
FFmpeg installed and available on the host PATH
Google Cloud service account JSON with write access to the target GCS bucket
Network access to the CDR recordings API
Getting started
git clone <repository-url>
cd CASRecordingFetchJob
dotnet restore
dotnet run --project CASRecordingFetchJob
By default the API listens on http://localhost:5178 (see Properties/launchSettings.json). Swagger UI is available when EnableSwaggerUI is true.

Configuration
Create CASRecordingFetchJob/appsettings.json (this file is gitignored). Example shape:

{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOUR_SERVER;Database=YOUR_DB;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "RecordingJobEnabled": true,
  "EnableSwaggerUI": true,
  "MaxDegreeOfParallelism": 4,
  "RecordingsBasePath": "C:\\Recordings",
  "S3RecordingBaseKey": "recordings",
  "SupportedAudioFormat": ".mp3",
  "RecordingsServerBasePath": "https://your-cdr-host/",
  "CdrUserName": "YOUR_CDR_USER",
  "CdrPassword": "YOUR_CDR_PASSWORD",
  "GoogleAuthFilePath": "C:\\path\\to\\gcs-service-account.json",
  "GCSBucketName": "your-gcs-bucket"
}
Key	Purpose
DefaultConnection	SQL Server connection string
RecordingJobEnabled	Master switch for the job
MaxDegreeOfParallelism	Concurrent conversations processed
RecordingsBasePath	Local content-server root (yyyy/M/d/{id}.mp3)
S3RecordingBaseKey	GCS object key prefix
RecordingsServerBasePath	CDR API base URL
CdrUserName / CdrPassword	CDR API credentials
GoogleAuthFilePath	Path to GCS service account JSON
GCSBucketName	Target GCS bucket
EnableSwaggerUI	Enable Swagger in the current environment
API
Execute recording job
GET /api/RecordingJob/Execute
Query parameter	Type	Description
startDate	DateTime?	Inclusive start (defaults to today)
endDate	DateTime?	Exclusive end (defaults to tomorrow)
companyId	int	Limit to one company (0 = all eligible)
leadtransitId	int	Process a single conversation (0 = date/company filter)
Optional header:

X-Correlation-ID: <guid-or-trace-id>
If omitted, a new correlation ID is generated and returned on the response.

Examples
# Process today's conversations for all eligible companies
curl "http://localhost:5178/api/RecordingJob/Execute"

# Date range for one company
curl "http://localhost:5178/api/RecordingJob/Execute?startDate=2025-09-01&endDate=2025-09-02&companyId=123"

# Single conversation
curl "http://localhost:5178/api/RecordingJob/Execute?leadtransitId=987654"
Successful responses include correlation ID, company IDs processed, conversation counts, success/failure counts, and per-recording flags (IsFetchedFromCDR, IsConvertedToMp3Variants, IsMovedToContentServer, IsMovedToGCS, etc.).

Project structure
CASRecordingFetchJob/
├── Controllers/          # RecordingJobController
├── Services/             # Job orchestration, CDR fetch, FFmpeg conversion
├── Helpers/              # GCS upload, logging helpers
├── Model/                # EF entities and DbContext
├── Repositories/         # Data access helpers
├── Program.cs            # DI, Serilog, Swagger
└── appsettings.json      # Local/secrets config (not committed)
Logging
Serilog writes to the console and to rolling daily files under C:\Logs\ (7-day retention). Use the X-Correlation-ID header to trace a single job run across log lines.

Notes
Companies with DisableRecordingDownloadFromJob or EnableRealTimeRecording company settings are excluded from bulk runs.
Existing files on the content server path are skipped (treated as already successful).
Agent trim timing uses phone-call / transfer data when agent-initiated calling is enabled for the company.
