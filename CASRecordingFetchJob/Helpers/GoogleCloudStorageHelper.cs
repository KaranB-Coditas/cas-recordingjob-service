using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using Grpc.Auth;
using Google.Cloud.SecretManager.V1;
using Renci.SshNet;

namespace CASRecordingFetchJob.Helpers
{
    public class GoogleCloudStorageHelper
    {
        private readonly StorageClient _storageClient;
        private readonly string _bucketName;
        private readonly SecretManagerServiceClient _secretClient;
        private readonly string _projectId;
        private readonly string _secreteName;
        private readonly UrlSigner _signer;

        public GoogleCloudStorageHelper(IConfiguration configuration)
        {
            var googleAuthFilePath = configuration.GetValue<string>("GoogleAuthFilePath")
                                     ?? throw new ArgumentNullException("GoogleAuthFilePath not configured");
            _bucketName = configuration.GetValue<string>("GCSBucketName")
                          ?? throw new ArgumentNullException("GCSBucketName not configured");

            var credential = GoogleCredential.FromFile(googleAuthFilePath);
            _storageClient = StorageClient.Create(credential);

            var builder = new SecretManagerServiceClientBuilder
            {
                Credential = credential
            };

            _secretClient = builder.Build();

            _storageClient = StorageClient.Create(credential);

            var serviceAccountCredential = credential.UnderlyingCredential as ServiceAccountCredential;
            _projectId = serviceAccountCredential?.ProjectId
                         ?? throw new InvalidOperationException("Project ID missing in Google Auth file");

            _secreteName = configuration.GetValue<string>("SecretName")
                ?? throw new InvalidOperationException("Secret Name is missing");

            _signer = UrlSigner.FromCredentialFile(googleAuthFilePath);
        }

        public async Task<PrivateKeyFile> GetPrivateKeyFromSecret()
        {
            var secretVersionName = new SecretVersionName(_projectId, _secreteName, "latest");
            var result = await _secretClient.AccessSecretVersionAsync(secretVersionName);

            using var keyStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(result.Payload.Data.ToStringUtf8()));
            return new PrivateKeyFile(keyStream);
        }

        public async Task UploadRecordingAsync(string key, Stream stream, string? contentType = null, CancellationToken cancellationToken = default)
        {
            if (stream == null || !stream.CanRead)
                throw new ArgumentException("Invalid stream provided", nameof(stream));

            await _storageClient.UploadObjectAsync(
                bucket: _bucketName,
                objectName: key,
                contentType: contentType,
                source: stream,
                cancellationToken: cancellationToken
            );
        }
        public async Task<Stream?> DownloadObject(string objectPath)
        {
            var obj = await _storageClient.GetObjectAsync(_bucketName, objectPath);
            if (obj == null)
                return null;

            var memoryStream = new MemoryStream();
            await _storageClient.DownloadObjectAsync(obj, memoryStream);

            memoryStream.Position = 0;
            return memoryStream;
        }

        public async Task<string> SignUrlAsync(string objectName, TimeSpan validity)
        {
            return await _signer.SignAsync(_bucketName, objectName, validity, HttpMethod.Get);
        }
    }
}
