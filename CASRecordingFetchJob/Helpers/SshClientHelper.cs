using CASRecordingFetchJob.Model;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Renci.SshNet;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace CASRecordingFetchJob.Helpers
{
    public class SshClientHelper
    {
        private readonly ILogger<SshClient> _logger;
        private readonly string _voipServerIP;
        private readonly string _sshUsername;
        private readonly string _voipServerFile;
        private readonly string _runScripCommand;
        private readonly GoogleCloudStorageHelper _gcsHelper;
        public static ConcurrentDictionary<string, RestorePcapResult> RestoreStatusMap = new();
        public SshClientHelper(IConfiguration configuration, ILogger<SshClient> logger, GoogleCloudStorageHelper gcsHelper)
        {
            _voipServerIP = configuration.GetValue<string>("VoipServerIPAddress")
                ?? throw new ArgumentNullException("VoipServerIP is missing in configuration");
            _sshUsername = configuration.GetValue<string>("SshUsername")
                ?? throw new ArgumentNullException("SshUsername is missing in configuration");
            _voipServerFile = configuration.GetValue<string>("VoipServerFile")
                ?? throw new ArgumentNullException("VoipServerFile is missing in configuration");
            _runScripCommand = configuration.GetValue<string>("SshCommands:RunTrial")
                ?? throw new ArgumentNullException("SshCommands:RunTrial is missing in configuration");
            _logger = logger;
            _gcsHelper = gcsHelper;
        }
        public async Task ConnectWithPrivateKeyAsync(string host, string username)
        {
            try
            {
                var privateKey = await _gcsHelper.GetPrivateKeyFromSecret();

                using var client = new SshClient(host, username, privateKey);
                client.Connect();

                Console.WriteLine("SSH connection established!");

                // Example command execution
                var cmd = client.RunCommand("whoami");
                Console.WriteLine($"Output: {cmd.Result}");

                client.Disconnect();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SSH connection failed: {ex.Message}");
            }
        }

        public async Task<bool> RunScriptAsync(IEnumerable<DateTime> dateList)
        {
            var privateKey = await _gcsHelper.GetPrivateKeyFromSecret();
            var dateArray = dateList
                .Select(date => $"{date:yyyy-MM-dd}/{date:HH}/{date:mm}")
                .Where(dateKey => !RestoreStatusMap.ContainsKey(dateKey))
                .ToList();

            string parameter = string.Join(" ", dateArray);
            var commandText = $"sudo su - -c \"cd /home/ec2-user/ && ./restore_recordings_async.sh {parameter}\""; //hard coded for now
            using var ssh = new SshClient(_voipServerIP, _sshUsername, privateKey);
            ssh.Connect();

            var cmd = ssh.CreateCommand(commandText);

            var asyncResult = cmd.BeginExecute();

            using var reader = new StreamReader(cmd.OutputStream);

            while (!asyncResult.IsCompleted || !reader.EndOfStream)
            {
                if (reader.Peek() > -1)
                {
                    var line = await reader.ReadLineAsync();
                    if (!string.IsNullOrEmpty(line))
                    {
                        try
                        {
                            var record = JsonConvert.DeserializeObject<RestoreResult>(line);
                            if (record != null)
                            {
                                var key = record.Date; 
                                if (!RestoreStatusMap.ContainsKey(key))
                                {
                                    RestoreStatusMap[key] = new RestorePcapResult { 
                                        Tar = record.Date,
                                        Status = "In-Progress",
                                        Reason = null,
                                        File = record.Date
                                    };
                                }

                                if (record.Status == "success")
                                {
                                    RestoreStatusMap[key].Status = "Success";
                                    RestoreStatusMap[key].Reason = null;
                                    Console.WriteLine($"Restored {record.Date}");
                                }
                                else
                                {
                                    RestoreStatusMap[key].Status = "Failed";
                                    RestoreStatusMap[key].Reason = record.Reason;
                                    Console.WriteLine($"Failed {record.Date} (Reason: {record.Reason})");
                                }
                            }
                        }
                        catch
                        {
                            Console.WriteLine($"Non-JSON output: {line}");
                        }
                    }
                }
                else
                {
                    await Task.Delay(1000);
                }
            }

            cmd.EndExecute(asyncResult);

            ssh.Disconnect();

            if (cmd.ExitStatus != 0)
            {
                throw new Exception($"Script failed. ExitStatus={cmd.ExitStatus}, Error={cmd.Error}");
            }

            return true;
        }

        public async Task<bool> RunRestoreRecordingPcapScriptAsync(string parameter)
        {
            var privateKey = await _gcsHelper.GetPrivateKeyFromSecret();

            var commandText = $"sudo su - -c \"cd /home/ec2-user/voipmonitor/ && ./restore_recordings_pcap_async.sh {parameter}\"";

            using var ssh = new SshClient(_voipServerIP, _sshUsername, privateKey);
            ssh.Connect();

            var cmd = ssh.CreateCommand(commandText);

            var asyncResult = cmd.BeginExecute();

            using var reader = new StreamReader(cmd.OutputStream);

            while (!asyncResult.IsCompleted || !reader.EndOfStream)
            {
                if (reader.Peek() > -1)
                {
                    var line = await reader.ReadLineAsync();
                    if (!string.IsNullOrEmpty(line))
                    {
                        try
                        {
                            var record = JsonConvert.DeserializeObject<RestorePcapResult>(line);
                            if (record != null)
                            {
                                var key = record.Tar;
                                if (!RestoreStatusMap.ContainsKey(key))
                                {
                                    RestoreStatusMap[key] = new RestorePcapResult
                                    {
                                        Tar = record.Tar,
                                        Status = "In-Progress",
                                        Reason = null,
                                        File = record.File
                                    };
                                }

                                if (record.Status == "success")
                                {
                                    RestoreStatusMap[key].Status = "Success";
                                    RestoreStatusMap[key].Reason = null;
                                    Console.WriteLine($"Restored {record.Tar}");
                                }
                                else
                                {
                                    RestoreStatusMap[key].Status = "Failed";
                                    RestoreStatusMap[key].Reason = record.Reason;
                                    Console.WriteLine($"Failed {record.Tar} (Reason: {record.Reason})");
                                }
                            }
                        }
                        catch
                        {
                            Console.WriteLine($"Non-JSON output: {line}");
                        }
                    }
                }
                else
                {
                    await Task.Delay(1000);
                }
            }

            cmd.EndExecute(asyncResult);

            ssh.Disconnect();

            if (cmd.ExitStatus != 0)
            {
                throw new Exception($"Script failed. ExitStatus={cmd.ExitStatus}, Error={cmd.Error}");
            }

            return true;
        }
    }
}
