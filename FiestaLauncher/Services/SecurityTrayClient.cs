using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FiestaLauncher.Models;
using FiestaLauncher.Shared.Models;

namespace FiestaLauncher.Services
{
    public class SecurityTrayClient
    {
        private const string PipeName = "NanOnline.SecurityTray.v1";
        private const int TrayReachabilityAttempts = 45;
        private const int TrayReachabilityDelayMs = 500;
        private const int TrayStartupGraceDelayMs = 1200;
        private const int TrayRestartSettleDelayMs = 800;
        private const int PipeConnectTimeoutMs = 2000;
        private static readonly UTF8Encoding Utf8NoBom = new(false);
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public async Task<(bool Success, string Message)> EnsureSessionAsync(
            ServerConfig config,
            string accessToken,
            string startToken,
            Dictionary<string, string> expectedHashes,
            string clientRoot,
            string gameProcessName)
        {
            var trayPath = Path.Combine(clientRoot, config.SecurityTrayExecutable);
            if (!File.Exists(trayPath))
            {
                return (false, $"Security tray not found: {trayPath}");
            }

            if (!await EnsureTrayAvailableAsync(trayPath, clientRoot))
            {
                return (false, "Security tray pipe is unavailable.");
            }

            var upsertCommand = new SecurityTrayCommand
            {
                Command = "upsert_session",
                AccessToken = accessToken,
                StartToken = startToken,
                HeartbeatUrl = config.LauncherHeartbeatUrl,
                SecurityEventUrl = config.LauncherSecurityEventUrl,
                ClientRoot = clientRoot,
                GameProcessName = gameProcessName,
                ExpectedHashes = expectedHashes,
                HeartbeatIntervalSeconds = 30
            };

            var response = await SendCommandAsync(upsertCommand);
            if (!response.Success)
            {
                return (false, response.Message);
            }

            var startResponse = await SendCommandAsync(new SecurityTrayCommand
            {
                Command = "start_watch"
            });

            return (startResponse.Success, startResponse.Message);
        }

        public async Task TryShutdownAsync(string trayProcessName = "NanoSecurityTray")
        {
            try
            {
                if (await IsTrayReachableAsync())
                {
                    await SendCommandAsync(new SecurityTrayCommand
                    {
                        Command = "shutdown"
                    });

                    for (var attempt = 0; attempt < 10; attempt++)
                    {
                        if (!await IsTrayReachableAsync())
                        {
                            return;
                        }

                        await Task.Delay(200);
                    }
                }
            }
            catch
            {
            }

            KillTrayProcesses(trayProcessName);
        }

        private async Task<bool> IsTrayReachableAsync()
        {
            try
            {
                var response = await SendCommandAsync(new SecurityTrayCommand
                {
                    Command = "ping"
                });
                return response.Success;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> EnsureTrayAvailableAsync(string trayPath, string clientRoot)
        {
            if (await IsTrayReachableAsync())
            {
                return true;
            }

            var trayProcessName = Path.GetFileNameWithoutExtension(trayPath);
            await TryShutdownAsync(trayProcessName);
            await Task.Delay(TrayRestartSettleDelayMs);

            if (!StartTrayProcess(trayPath, clientRoot))
            {
                return false;
            }

            await Task.Delay(TrayStartupGraceDelayMs);

            for (var attempt = 0; attempt < TrayReachabilityAttempts; attempt++)
            {
                if (await IsTrayReachableAsync())
                {
                    return true;
                }

                if (attempt == 10 && !IsTrayProcessRunning(trayProcessName))
                {
                    if (!StartTrayProcess(trayPath, clientRoot))
                    {
                        break;
                    }
                }

                await Task.Delay(TrayReachabilityDelayMs);
            }

            KillTrayProcesses(trayProcessName);
            return false;
        }

        private async Task<SecurityTrayResponse> SendCommandAsync(SecurityTrayCommand command)
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(PipeConnectTimeoutMs);

            using var writer = new StreamWriter(client, Utf8NoBom, 4096, leaveOpen: true)
            {
                AutoFlush = true
            };
            using var reader = new StreamReader(client, Utf8NoBom, false, 4096, leaveOpen: false);

            var commandJson = JsonSerializer.Serialize(command, _jsonOptions);
            await writer.WriteLineAsync(commandJson);

            var responseJson = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return new SecurityTrayResponse
                {
                    Success = false,
                    Message = "Security tray returned no response."
                };
            }

            return JsonSerializer.Deserialize<SecurityTrayResponse>(responseJson, _jsonOptions)
                ?? new SecurityTrayResponse
                {
                    Success = false,
                    Message = "Security tray returned an invalid response."
                };
        }

        private static bool StartTrayProcess(string trayPath, string clientRoot)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = trayPath,
                    WorkingDirectory = clientRoot,
                    UseShellExecute = false
                });

                if (process is null)
                {
                    return false;
                }

                try
                {
                    process.WaitForInputIdle(5000);
                }
                catch
                {
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsTrayProcessRunning(string trayProcessName)
        {
            if (string.IsNullOrWhiteSpace(trayProcessName))
            {
                return false;
            }

            foreach (var process in Process.GetProcessesByName(trayProcessName))
            {
                try
                {
                    if (!process.HasExited)
                    {
                        return true;
                    }
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }

            return false;
        }

        private static void KillTrayProcesses(string trayProcessName)
        {
            if (string.IsNullOrWhiteSpace(trayProcessName))
            {
                return;
            }

            foreach (var process in Process.GetProcessesByName(trayProcessName))
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                        process.WaitForExit(2000);
                    }
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
    }
}
