using System.Diagnostics;
using System.Drawing;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using FiestaLauncher.Shared.Models;
using FiestaLauncher.Shared.Security;
using Timer = System.Threading.Timer;

const string InstanceMutexName = @"Global\NanOnline.SecurityTray.Instance";
using var instanceMutex = new Mutex(true, InstanceMutexName, out var createdNew);
if (!createdNew)
{
    return;
}

try
{
    ApplicationConfiguration.Initialize();
    Application.Run(new SecurityTrayContext());
}
finally
{
    try
    {
        instanceMutex.ReleaseMutex();
    }
    catch
    {
    }
}

internal sealed class SecurityTrayContext : ApplicationContext
{
    private const string PipeName = "NanOnline.SecurityTray.v1";
    private static readonly TimeSpan BusyPipeRetryDelay = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan GeneralPipeRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly NotifyIcon _notifyIcon;
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly SemaphoreSlim _watchLock = new(1, 1);
    private readonly CancellationTokenSource _shutdownTokenSource = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private Timer? _watchTimer;
    private string _accessToken = string.Empty;
    private string _startToken = string.Empty;
    private string _heartbeatUrl = string.Empty;
    private string _securityEventUrl = string.Empty;
    private string _clientRoot = string.Empty;
    private string _gameProcessName = "Nano";
    private int _heartbeatIntervalSeconds = 30;
    private Dictionary<string, string> _expectedHashes = new(StringComparer.OrdinalIgnoreCase);
    private bool _isRevoked;
    private string _lastPipeErrorKey = string.Empty;
    private DateTime _lastPipeErrorLoggedAtUtc = DateTime.MinValue;
    private int _pipeReadyLogged;

    public SecurityTrayContext()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Visible = true,
            Text = "NanOnline Security Tray",
            ContextMenuStrip = BuildMenu()
        };

        WriteLog("Security tray started.");
        _ = Task.Factory.StartNew(
            () => RunPipeLoopAsync(_shutdownTokenSource.Token),
            _shutdownTokenSource.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
    }

    private static Icon LoadIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Nano.ico");
        return File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Shield;
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Client Folder", null, (_, _) =>
        {
            if (Directory.Exists(_clientRoot))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _clientRoot,
                    UseShellExecute = true
                });
            }
        });
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        return menu;
    }

    private async Task RunPipeLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = CreatePipeServer();
                LogPipeReadyOnce();

                await pipe.WaitForConnectionAsync(cancellationToken);
                ClearPipeErrorState();
                var connectedPipe = pipe;
                _ = Task.Run(() => HandleConnectionAsync(connectedPipe, cancellationToken), CancellationToken.None);
                pipe = null;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException ex)
            {
                LogPipeLoopError(ex);
                await DelayAfterPipeFailureAsync(ex, cancellationToken);
            }
            catch (TimeoutException ex)
            {
                LogPipeLoopError(ex);
                await DelayAfterPipeFailureAsync(ex, cancellationToken);
            }
            catch (Exception ex)
            {
                LogPipeLoopError(ex);
                await DelayAfterPipeFailureAsync(ex, cancellationToken);
            }
            finally
            {
                pipe?.Dispose();
            }
        }
    }

    private NamedPipeServerStream CreatePipeServer()
    {
        return new NamedPipeServerStream(
            PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        try
        {
            using var reader = new StreamReader(pipe, Utf8NoBom, false, 4096, leaveOpen: true);
            using var writer = new StreamWriter(pipe, Utf8NoBom, 4096, leaveOpen: true)
            {
                AutoFlush = true
            };

            var payload = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            var command = JsonSerializer.Deserialize<SecurityTrayCommand>(payload ?? string.Empty, _jsonOptions)
                ?? new SecurityTrayCommand();
            var response = await HandleCommandAsync(command);

            if (pipe.CanWrite)
            {
                var responseJson = JsonSerializer.Serialize(response, _jsonOptions);
                await writer.WriteLineAsync(responseJson);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException ex)
        {
            LogPipeLoopError(ex);
        }
        catch (TimeoutException ex)
        {
            LogPipeLoopError(ex);
        }
        catch (Exception ex)
        {
            LogPipeLoopError(ex);
        }
        finally
        {
            pipe.Dispose();
        }
    }

    private async Task<SecurityTrayResponse> HandleCommandAsync(SecurityTrayCommand command)
    {
        await _commandLock.WaitAsync();
        try
        {
            switch ((command.Command ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "upsert_session":
                    _accessToken = command.AccessToken ?? string.Empty;
                    _startToken = command.StartToken ?? string.Empty;
                    _heartbeatUrl = command.HeartbeatUrl ?? string.Empty;
                    _securityEventUrl = command.SecurityEventUrl ?? string.Empty;
                    _clientRoot = command.ClientRoot ?? string.Empty;
                    _gameProcessName = string.IsNullOrWhiteSpace(command.GameProcessName) ? "Nano" : command.GameProcessName;
                    _heartbeatIntervalSeconds = Math.Max(10, command.HeartbeatIntervalSeconds);
                    _expectedHashes = new Dictionary<string, string>(
                        command.ExpectedHashes ?? new Dictionary<string, string>(),
                        StringComparer.OrdinalIgnoreCase);
                    _isRevoked = false;
                    _notifyIcon.Text = "NanOnline Security active";
                    return new SecurityTrayResponse { Success = true, Message = "Session aktualisiert." };

                case "ping":
                    return new SecurityTrayResponse { Success = true, Message = "pong" };

                case "start_watch":
                    StartWatchTimer();
                    _ = RunWatchCycleAsync();
                    return new SecurityTrayResponse { Success = true, Message = "Dateiueberwachung gestartet." };

                case "stop_watch":
                    _watchTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                    return new SecurityTrayResponse { Success = true, Message = "Dateiueberwachung gestoppt." };

                case "logout":
                    _watchTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                    _accessToken = string.Empty;
                    _startToken = string.Empty;
                    _isRevoked = false;
                    _notifyIcon.Text = "NanOnline Security Tray";
                    return new SecurityTrayResponse { Success = true, Message = "Session entfernt." };

                case "shutdown":
                    _ = Task.Run(ExitThread);
                    return new SecurityTrayResponse { Success = true, Message = "Tray wird beendet." };

                default:
                    return new SecurityTrayResponse { Success = false, Message = "Unbekannter Tray-Befehl." };
            }
        }
        finally
        {
            _commandLock.Release();
        }
    }

    private void StartWatchTimer()
    {
        _watchTimer ??= new Timer(async _ => await RunWatchCycleAsync(), null, Timeout.Infinite, Timeout.Infinite);
        _watchTimer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(_heartbeatIntervalSeconds));
    }

    private async Task RunWatchCycleAsync()
    {
        if (_isRevoked || string.IsNullOrWhiteSpace(_clientRoot) || _expectedHashes.Count == 0)
        {
            return;
        }

        if (!await _watchLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            var processRunning = IsGameRunning();
            foreach (var relativePath in LauncherFileWatchCatalog.RelativePaths)
            {
                if (!_expectedHashes.TryGetValue(relativePath, out var expectedHash) || string.IsNullOrWhiteSpace(expectedHash))
                {
                    continue;
                }

                var fullPath = Path.Combine(_clientRoot, relativePath);
                var actualHash = FileHashUtility.CalculateSha256(fullPath);
                if (string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                await ReportViolationAsync(relativePath, expectedHash, actualHash, processRunning);
                _isRevoked = true;
                _watchTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                return;
            }

            if (!string.IsNullOrWhiteSpace(_heartbeatUrl) && !string.IsNullOrWhiteSpace(_accessToken))
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                var response = await client.PostAsync(
                    _heartbeatUrl,
                    new StringContent(
                        JsonSerializer.Serialize(new LauncherHeartbeatRequest
                        {
                            SessionToken = _accessToken
                        }, _jsonOptions),
                        Encoding.UTF8,
                        "application/json"));

                var json = await response.Content.ReadAsStringAsync();
                var heartbeat = JsonSerializer.Deserialize<LauncherHeartbeatResponse>(json, _jsonOptions);

                if (heartbeat is not null && (!heartbeat.Success || !heartbeat.IsActive))
                {
                    _isRevoked = true;
                    ShowWarning("Die Launcher-Session wurde serverseitig widerrufen.");
                    _watchTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }
        }
        catch (Exception ex)
        {
            WriteLog("Watch cycle failed: " + ex.Message);
        }
        finally
        {
            _watchLock.Release();
        }
    }

    private async Task ReportViolationAsync(string relativePath, string expectedHash, string actualHash, bool processRunning)
    {
        ShowWarning($"Dateipruefung fehlgeschlagen: {relativePath}");

        if (string.IsNullOrWhiteSpace(_securityEventUrl) || string.IsNullOrWhiteSpace(_accessToken))
        {
            return;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var payload = new SecurityEventRequest
            {
                SessionToken = _accessToken,
                EventType = "file_mismatch",
                FilePath = relativePath,
                ExpectedSha256 = expectedHash,
                ActualSha256 = actualHash,
                ProcessRunning = processRunning,
                OccurredAt = DateTime.UtcNow.ToString("O")
            };

            await client.PostAsync(
                _securityEventUrl,
                new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json"));
        }
        catch (Exception ex)
        {
            WriteLog("Security event upload failed: " + ex.Message);
        }
    }

    private bool IsGameRunning()
    {
        var processName = Path.GetFileNameWithoutExtension(_gameProcessName);
        return !string.IsNullOrWhiteSpace(processName) && Process.GetProcessesByName(processName).Length > 0;
    }

    private void ShowWarning(string message)
    {
        WriteLog(message);
        _notifyIcon.Text = "NanOnline Security warning";
        _notifyIcon.ShowBalloonTip(4000, "NanOnline Security", message, ToolTipIcon.Warning);
    }

    private static bool IsPipeBusy(IOException exception)
    {
        return exception.Message.Contains("Pipeinstanzen", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("pipe instances", StringComparison.OrdinalIgnoreCase);
    }

    private async Task DelayAfterPipeFailureAsync(Exception exception, CancellationToken cancellationToken)
    {
        var delay = exception is IOException ioException && IsPipeBusy(ioException)
            ? BusyPipeRetryDelay
            : GeneralPipeRetryDelay;

        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void LogPipeLoopError(Exception exception)
    {
        var errorKey = exception is IOException ioException && IsPipeBusy(ioException)
            ? "pipe_busy"
            : exception is TimeoutException
                ? "pipe_timeout"
            : exception.GetType().Name + ":" + exception.Message;

        var nowUtc = DateTime.UtcNow;
        if (string.Equals(errorKey, _lastPipeErrorKey, StringComparison.Ordinal) &&
            nowUtc - _lastPipeErrorLoggedAtUtc < TimeSpan.FromMinutes(1))
        {
            return;
        }

        _lastPipeErrorKey = errorKey;
        _lastPipeErrorLoggedAtUtc = nowUtc;

        if (exception is IOException busyException && IsPipeBusy(busyException))
        {
            WriteLog("Tray pipe is busy. Retrying connection loop.");
            return;
        }

        if (exception is TimeoutException)
        {
            WriteLog("Tray pipe timed out while waiting for a complete command. Retrying connection loop.");
            return;
        }

        WriteLog("Pipe loop failed: " + exception.GetType().Name + ": " + exception.Message);
    }

    private void ClearPipeErrorState()
    {
        _lastPipeErrorKey = string.Empty;
        _lastPipeErrorLoggedAtUtc = DateTime.MinValue;
    }

    private void LogPipeReadyOnce()
    {
        if (Interlocked.Exchange(ref _pipeReadyLogged, 1) == 0)
        {
            WriteLog("Tray pipe listener ready.");
        }
    }

    private void WriteLog(string message)
    {
        var fallbackRoot = Directory.Exists(_clientRoot) ? _clientRoot : AppContext.BaseDirectory;
        LocalLogWriter.Write("nano_security_tray.log", message, fallbackRoot);
    }

    protected override void ExitThreadCore()
    {
        _shutdownTokenSource.Cancel();
        _watchTimer?.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _commandLock.Dispose();
        _watchLock.Dispose();
        _shutdownTokenSource.Dispose();
        base.ExitThreadCore();
    }
}
