<a name="top"></a>

# CAS Recording Job Service

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![language](https://img.shields.io/badge/language-C%23-239120)](https://learn.microsoft.com/dotnet/csharp/)
[![framework](https://img.shields.io/badge/ASP.NET%20Core-Web%20API-512BD4)](https://learn.microsoft.com/aspnet/core/)
[![OS](https://img.shields.io/badge/OS-Windows%20%7C%20Linux-0078D4)](#-how-to-build)
[![storage](https://img.shields.io/badge/storage-Google%20Cloud%20Storage-4285F4)](https://cloud.google.com/storage)
[![SQL Server](https://img.shields.io/badge/database-SQL%20Server-CC2927)](https://www.microsoft.com/sql-server)
[![Redis](https://img.shields.io/badge/lock-Redis-DC382D)](https://redis.io/)
[![Swagger](https://img.shields.io/badge/API-Swagger-85EA2D)](#-api-endpoints)
[![GitHub last commit](https://img.shields.io/github/last-commit/KaranB-Coditas/cas-recordingjob-service)](https://github.com/KaranB-Coditas/cas-recordingjob-service)
[![GitHub release](https://img.shields.io/github/v/release/KaranB-Coditas/cas-recordingjob-service)](https://github.com/KaranB-Coditas/cas-recordingjob-service/releases)

⭐ Star this repo on GitHub if it helps your team — your support keeps the project moving. 🙏

[![Share](https://img.shields.io/badge/share-000000?logo=x&logoColor=white)](https://x.com/intent/tweet?text=Check%20out%20this%20project%20on%20GitHub:%20https://github.com/KaranB-Coditas/cas-recordingjob-service)
[![Share](https://img.shields.io/badge/share-1877F2?logo=facebook&logoColor=white)](https://www.facebook.com/sharer/sharer.php?u=https://github.com/KaranB-Coditas/cas-recordingjob-service)
[![Share](https://img.shields.io/badge/share-0A66C2?logo=linkedin&logoColor=white)](https://www.linkedin.com/sharing/share-offsite/?url=https://github.com/KaranB-Coditas/cas-recordingjob-service)
[![Share](https://img.shields.io/badge/share-FF4500?logo=reddit&logoColor=white)](https://www.reddit.com/submit?title=Check%20out%20this%20project%20on%20GitHub:%20https://github.com/KaranB-Coditas/cas-recordingjob-service)
[![Share](https://img.shields.io/badge/share-0088CC?logo=telegram&logoColor=white)](https://t.me/share/url?url=https://github.com/KaranB-Coditas/cas-recordingjob-service&text=Check%20out%20this%20project%20on%20GitHub)

**Fetch, restore, process, and publish ConnectAndSell call recordings — from VoIP/CDR sources through audio processing into Google Cloud Storage with signed URL delivery.**

## Table of Contents
- [About](#-about)
- [Features](#-features)
- [Architecture](#-architecture)
- [Quickstart](#-quickstart)
- [API Endpoints](#-api-endpoints)
- [Configuration](#-configuration)
- [How to Build](#-how-to-build)
- [Project Structure](#-project-structure)
- [Feedback and Contributions](#-feedback-and-contributions)
- [License](#-license)

## 🚀 About

**CAS Recording Job Service** (`CASRecordingFetchJob`) is an ASP.NET Core Web API that runs ConnectAndSell recording fetch jobs. It pulls conversation metadata from SQL Server, restores CDR media from the VoIP server when needed, processes audio (trim, dual-consent handling, pause announcements), and uploads the result to Google Cloud Storage.

- **On-demand and scheduled:** trigger jobs by date range, company, or lead transit ID, or enable the daily hosted job.
- **CDR restore aware:** restores older recordings from the VoIP spool over SSH when retention windows require it.
- **Audio pipeline:** FFmpeg-based processing with optional agent-part trimming, dual-consent flows, and WAV retention.
- **Cloud delivery:** uploads to GCS and can generate time-limited signed URLs for playback.
- **Safe concurrency:** Redis distributed locks prevent overlapping job runs; Serilog provides correlation-id enriched logs.

## ✨ Features

- Execute recording jobs for a date range, company, or single `LeadtransitId`
- Restore CDR recordings by lead transit ID or by date(s) on the VoIP server
- Generate GCS signed URLs for stored recordings
- Parallel download/processing controlled by `MaxDegreeOfParallelism`
- Optional daily background job via `DailyJob` settings
- Swagger UI for local exploration (`EnableSwaggerUI`)
- Correlation ID middleware for end-to-end request tracing
- Retry helpers (Polly) and SSH access to the VoIP host (SSH.NET + Google Secret Manager for keys)

## 🏗 Architecture

| Layer | Responsibility |
|:-|:-|
| `RecordingJobController` | HTTP API for execute, signed URL, and CDR restore operations |
| `RecordingJobService` | Orchestrates job validation, locking, restore, process, and upload |
| `RecordingDataService` | Loads conversation / phone-call data and company settings from SQL Server |
| `RecordingDownloader` | Fetches media from the CDR/VoIP path and runs bulk restore flows |
| `RecordingProcessor` | Audio transforms (trim, dual consent, pause announcement, format) |
| `RecordingMover` | Moves processed files into the GCS layout |
| `DailyJobHostedService` | Optional scheduled daily execution |
| `RedisLockManager` | Distributed lock so only one job owner runs at a time |
| `GoogleCloudStorageHelper` | GCS upload and signed URL generation |
| `SshClientHelper` | SSH commands against the VoIP server |

**Typical flow:** API request → acquire Redis lock → query Castanet DB → restore CDR if outside retention → download → process with FFmpeg → upload to GCS → optionally return signed URL → persist job result.

## ⚡ Quickstart

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- SQL Server access to the Castanet database
- Redis instance for distributed locking
- Google Cloud credentials with Storage access
- FFmpeg available to the process (via FFMpegCore)
- Network/SSH access to the VoIP / CDR host when restore is required

### Run locally

```shell
git clone https://github.com/KaranB-Coditas/cas-recordingjob-service.git
cd cas-recordingjob-service

# Update CASRecordingFetchJob/appsettings.Development.json with your environment values
dotnet restore
dotnet run --project CASRecordingFetchJob
```

Swagger UI (when enabled): [http://localhost:5178/swagger](http://localhost:5178/swagger)

HTTPS profile also listens on `https://localhost:7108`.

### Trigger a job

```http
POST /api/RecordingJob/ExecuteAsync
Content-Type: application/json

{
  "startDate": "2026-09-08",
  "endDate": "2026-09-08",
  "companyId": 0,
  "leadtransitId": 0,
  "isRestoreCdrRecordingEnabled": false,
  "addPauseAnnouncement": false,
  "isDualConsent": false,
  "generateSignedUrl": false
}
```

## 📡 API Endpoints

| Method | Route | Description |
|:-|:-|:-|
| `POST` | `/api/RecordingJob/ExecuteAsync` | Run the recording fetch/process job for a date range, company, and/or lead transit ID |
| `POST` | `/api/RecordingJob/GenerateSignedUrlAsync` | Create a GCS signed URL for an existing recording |
| `POST` | `/api/RecordingJob/RestoreCdrRecordingByLeadtransitIdAsync?leadtransitId={id}` | Restore a single CDR recording on the VoIP server |
| `POST` | `/api/RecordingJob/RestoreCDRRecordingsByDateAsync?rawDateInput={dates}` | Restore CDR files for one or more dates (comma-separated) |

### Execute payload fields

| Field | Type | Notes |
|:-|:-|:-|
| `startDate` / `endDate` | `DateTime?` | Defaults to yesterday when omitted |
| `companyId` | `int` | `0` = all companies |
| `leadtransitId` | `int` | `0` = batch mode; set to target one conversation |
| `isRestoreCdrRecordingEnabled` | `bool` | Force CDR restore even when within retention |
| `addPauseAnnouncement` | `bool` | Inject pause announcement during processing |
| `isDualConsent` | `bool` | Dual-consent recording path |
| `generateSignedUrl` | `bool` | Return/generate signed URL as part of the job |

## ⚙ Configuration

Configure via `appsettings.json` / `appsettings.Development.json` or environment variables. Do **not** commit real credentials.

| Key | Purpose |
|:-|:-|
| `ConnectionStrings:DefaultConnection` | SQL Server (Castanet) connection string |
| `RedisServer` | Redis endpoint for distributed locks |
| `RecordingsBasePath` | Local/temp working directory for recordings |
| `RecordingsServerBasePath` | CDR/HTTP base URL for media fetch |
| `SupportedAudioFormat` | Output format (e.g. `mp3`) |
| `GoogleAuthFilePath` | Path to GCS service account JSON |
| `GCSBucketName` | Target GCS bucket |
| `S3RecordingBaseKey` | Object key prefix inside the bucket |
| `SignedUrlExpiredTimeHours` | Signed URL lifetime (default `168`) |
| `MaxDegreeOfParallelism` | Parallel processing limit |
| `TrimAgentPart` | Trim pre-agent audio when enabled |
| `ProcessDualConsentRecording` | Enable dual-consent processing |
| `SaveWAVRecording` | Keep WAV alongside converted audio |
| `DailyJob:Enabled` / `DailyJob:RunAt` | Scheduled daily job toggle and local time |
| `CdrRestoreOnJob` | Always attempt CDR restore during jobs |
| `CdrDataRetentionDays` | Age threshold that requires restore |
| `VoipServerIPAddress` / `SshUsername` / `SecretName` | VoIP SSH restore settings |
| `VoipServerBasePath` / `VoipServerTempPath` | Paths on the VoIP host |
| `EnableSwaggerUI` | Expose Swagger in the current environment |

> [!IMPORTANT]
> Treat `appsettings*.json` as sensitive. Prefer user secrets, environment variables, or a secret manager for connection strings, CDR credentials, and Google auth paths in shared environments.

## 📝 How to Build

```shell
# Clone the repository
git clone https://github.com/KaranB-Coditas/cas-recordingjob-service.git
cd cas-recordingjob-service

# Verify the SDK
dotnet --version

# Restore and build
dotnet restore
dotnet build CASRecordingFetchJob.sln

# Optional: publish
dotnet publish CASRecordingFetchJob/CASRecordingFetchJob.csproj -c Release -o ./publish
```

## 📂 Project Structure

```text
cas-recordingjob-service/
├── CASRecordingFetchJob.sln
├── CASRecordingFetchJob/
│   ├── Controllers/          # RecordingJob HTTP API
│   ├── Services/             # Job orchestration, download, process, move, daily host
│   ├── Repositories/         # Data access helpers
│   ├── Helpers/              # GCS, SSH, Redis lock, retry, correlation
│   ├── Middleware/           # Correlation ID middleware
│   ├── Model/                # EF context, payloads, domain entities
│   ├── Program.cs
│   └── appsettings*.json
├── SQLScript/                # SQL helpers for job results / diagnostics
└── restore_recordings_pcap_async.sh
```

## 🤝 Feedback and Contributions

This service is used in real recording pipelines. Gaps you hit in restore, dual consent, or GCS delivery are the ones that matter most.

> [!IMPORTANT]
> When reporting an issue, include the correlation ID from the logs, the `LeadtransitId` or date range, and whether CDR restore was required. That turns a guess into a fix.

Open an [issue](https://github.com/KaranB-Coditas/cas-recordingjob-service/issues) or submit a pull request against `main`.

## 📃 License

Proprietary — ConnectAndSell / internal use unless otherwise agreed. Contact the repository owners for redistribution terms.

[Back to top](#top)
