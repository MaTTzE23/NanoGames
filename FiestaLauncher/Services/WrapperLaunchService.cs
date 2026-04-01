using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FiestaLauncher.Shared.Models;

namespace FiestaLauncher.Services
{
    public class WrapperLaunchService
    {
        private static readonly UTF8Encoding Utf8NoBom = new(false);

        public async Task<(bool Success, string Message, Process? Process)> LaunchAsync(
            string wrapperPath,
            string workingDirectory,
            WrapperLaunchPayload payload)
        {
            if (string.IsNullOrWhiteSpace(wrapperPath) || !File.Exists(wrapperPath))
            {
                return (false, $"Wrapper executable not found: {wrapperPath}", null);
            }

            var launchId = string.IsNullOrWhiteSpace(payload.LaunchId)
                ? Guid.NewGuid().ToString("N")
                : payload.LaunchId;

            payload.LaunchId = launchId;
            var pipeName = "NanOnline.StartPayload." + launchId;

            try
            {
                using var pipe = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.Out,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = wrapperPath,
                    Arguments = $"--launch-id {launchId}",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null)
                {
                    return (false, "Wrapper process could not be started.", null);
                }

                await pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(15));

                var payloadJson = JsonSerializer.Serialize(payload);
                using var writer = new StreamWriter(pipe, Utf8NoBom, 4096, leaveOpen: true);
                await writer.WriteAsync(payloadJson);
                await writer.FlushAsync();

                return (true, "Wrapper process started.", process);
            }
            catch (Exception ex)
            {
                return (false, $"Wrapper launch failed: {ex.Message}", null);
            }
        }
    }
}
