using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using FiestaLauncher.Shared.Models;
using FiestaLauncher.Shared.Security;

var launchId = ParseLaunchId(args);
if (string.IsNullOrWhiteSpace(launchId))
{
    return 10;
}

var pipeName = "NanOnline.StartPayload." + launchId;

try
{
    var payload = await ReadPayloadAsync(pipeName);
    if (payload is null)
    {
        return 11;
    }

    var gameBinaryPath = Path.IsPathRooted(payload.GameBinary)
        ? payload.GameBinary
        : Path.Combine(payload.ClientRoot, payload.GameBinary);

    if (!File.Exists(gameBinaryPath))
    {
        WriteLog(payload.ClientRoot, "Game binary missing: " + gameBinaryPath);
        return 12;
    }

    var arguments = BuildArguments(payload);
    WriteLog(payload.ClientRoot, $"Launching {Path.GetFileName(gameBinaryPath)} through wrapper.");

    var process = Process.Start(new ProcessStartInfo
    {
        FileName = gameBinaryPath,
        WorkingDirectory = payload.ClientRoot,
        Arguments = arguments,
        UseShellExecute = false,
        CreateNoWindow = true
    });

    if (process is null)
    {
        WriteLog(payload.ClientRoot, "Game process could not be created.");
        return 13;
    }

    await Task.Delay(1500);
    if (process.HasExited)
    {
        WriteLog(payload.ClientRoot, "Game process exited immediately with code " + process.ExitCode + ".");
        return 14;
    }

    WriteLog(payload.ClientRoot, "Game process started successfully.");
    return 0;
}
catch (Exception ex)
{
    WriteLog(AppContext.BaseDirectory, ex.GetType().Name + ": " + ex.Message);
    return 99;
}

static string ParseLaunchId(string[] arguments)
{
    for (var index = 0; index < arguments.Length; index++)
    {
        if (!string.Equals(arguments[index], "--launch-id", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (index + 1 < arguments.Length)
        {
            return arguments[index + 1];
        }
    }

    return string.Empty;
}

static async Task<WrapperLaunchPayload?> ReadPayloadAsync(string pipeName)
{
    using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.Asynchronous);
    await pipe.ConnectAsync(15000);

    using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: false);
    var payloadJson = await reader.ReadToEndAsync();
    if (string.IsNullOrWhiteSpace(payloadJson))
    {
        return null;
    }

    return JsonSerializer.Deserialize<WrapperLaunchPayload>(payloadJson);
}

static string BuildArguments(WrapperLaunchPayload payload)
{
    return string.Join(
        ' ',
        new[]
        {
            "-i", Quote(payload.LoginHost),
            "-p", payload.LoginPort.ToString(),
            "-osk_server", Quote(payload.OskServer),
            "-osk_token", Quote(payload.StartToken),
            "-osk_store", Quote(payload.OskStore),
        });
}

static string Quote(string value)
{
    return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
}

static void WriteLog(string rootPath, string message)
{
    LocalLogWriter.Write("nano_wrapper.log", message, rootPath);
}
