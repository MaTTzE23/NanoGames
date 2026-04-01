using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using FiestaLauncher.Models;

namespace FiestaLauncher.Services
{
    public class GameLauncherService
    {
        private readonly ServerConfig _config;

        public GameLauncherService(ServerConfig config)
        {
            _config = config;
        }

        public (bool Success, string Message, Process? Process) LaunchGame(string? token = null, string? accountId = null)
        {
            try
            {
                var gameDir = GetGameDirectory();
                var gamePath = Path.Combine(gameDir, _config.GameExecutable);

                if (!File.Exists(gamePath))
                {
                    return (false, string.Format("Game executable was not found: {0}", gamePath), null);
                }

                var processInfo = BuildProcessStartInfo(gameDir, gamePath, token, accountId);

                var process = Process.Start(processInfo);

                if (process != null)
                {
                    return (true, "Game launched successfully.", process);
                }

                return (false, "Game process could not be started.", null);
            }
            catch (Exception ex)
            {
                return (false, string.Format("Failed to launch the game: {0}", ex.Message), null);
            }
        }

        private ProcessStartInfo BuildProcessStartInfo(string gameDir, string gamePath, string? token, string? accountId)
        {
            var extension = Path.GetExtension(gamePath);
            var launcherArguments = BuildArguments(gamePath, token, accountId);

            if (string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase))
            {
                var batchCommand = string.IsNullOrWhiteSpace(launcherArguments)
                    ? string.Format("\"{0}\"", gamePath)
                    : string.Format("\"{0}\" {1}", gamePath, launcherArguments);

                return new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    WorkingDirectory = gameDir,
                    Arguments = string.Format("/c {0}", batchCommand),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
            }

            return new ProcessStartInfo
            {
                FileName = gamePath,
                WorkingDirectory = gameDir,
                Arguments = launcherArguments,
                UseShellExecute = true
            };
        }

        private string BuildArguments(string gamePath, string? token, string? accountId)
        {
            var args = new StringBuilder();
            var extension = Path.GetExtension(gamePath);

            if (!string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase))
            {
                AppendArgumentString(args, ResolveBootstrapArguments(gamePath));
            }

            if (!string.IsNullOrEmpty(token))
            {
                args.AppendFormat("/t:{0} ", token);
            }

            if (!string.IsNullOrEmpty(accountId))
            {
                args.AppendFormat("/a:{0} ", accountId);
            }

            return args.ToString().Trim();
        }

        private string ResolveBootstrapArguments(string gamePath)
        {
            if (!string.IsNullOrWhiteSpace(_config.GameArguments))
            {
                return _config.GameArguments.Trim();
            }

            var siblingBatchPath = Path.ChangeExtension(gamePath, ".bat");
            if (!File.Exists(siblingBatchPath))
            {
                return string.Empty;
            }

            if (!TryReadBatchLaunchCommand(siblingBatchPath, out var command, out var arguments))
            {
                return string.Empty;
            }

            var expectedName = Path.GetFileNameWithoutExtension(gamePath);
            var commandName = Path.GetFileNameWithoutExtension(command);
            if (!string.Equals(expectedName, commandName, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return arguments;
        }

        private static void AppendArgumentString(StringBuilder args, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            args.Append(value.Trim());
            args.Append(' ');
        }

        private string GetGameDirectory()
        {
            if (!string.IsNullOrEmpty(_config.GameDirectory))
                return _config.GameDirectory;
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        public bool IsGameRunning()
        {
            var binaryProcessName = Path.GetFileNameWithoutExtension(_config.GameBinaryExecutable);
            if (!string.IsNullOrWhiteSpace(binaryProcessName) &&
                Process.GetProcessesByName(binaryProcessName).Length > 0)
            {
                return true;
            }

            var configuredProcessName = Path.GetFileNameWithoutExtension(_config.GameExecutable);
            if (!string.IsNullOrWhiteSpace(configuredProcessName) &&
                Process.GetProcessesByName(configuredProcessName).Length > 0)
            {
                return true;
            }

            var batchTargetProcessName = ResolveBatchTargetProcessName();
            if (!string.IsNullOrWhiteSpace(batchTargetProcessName) &&
                !string.Equals(batchTargetProcessName, configuredProcessName, StringComparison.OrdinalIgnoreCase))
            {
                return Process.GetProcessesByName(batchTargetProcessName).Length > 0;
            }

            return false;
        }

        private string ResolveBatchTargetProcessName()
        {
            var extension = Path.GetExtension(_config.GameExecutable);
            if (!string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var batchPath = Path.Combine(GetGameDirectory(), _config.GameExecutable);
            if (!File.Exists(batchPath))
            {
                return string.Empty;
            }

            if (!TryReadBatchLaunchCommand(batchPath, out var command, out _))
            {
                return string.Empty;
            }

            return Path.GetFileNameWithoutExtension(command);
        }

        private static bool TryReadBatchLaunchCommand(string batchPath, out string command, out string arguments)
        {
            foreach (var rawLine in File.ReadLines(batchPath))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) ||
                    line.StartsWith("@echo", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("rem ", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("::", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("cd ", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TrySplitCommandAndArguments(line, out command, out arguments))
                {
                    return true;
                }
            }

            command = string.Empty;
            arguments = string.Empty;
            return false;
        }

        private static bool TrySplitCommandAndArguments(string line, out string command, out string arguments)
        {
            command = string.Empty;
            arguments = string.Empty;

            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            line = line.Trim();
            if (line.StartsWith("\"", StringComparison.Ordinal))
            {
                var closingQuoteIndex = line.IndexOf('"', 1);
                if (closingQuoteIndex > 0)
                {
                    command = line.Substring(1, closingQuoteIndex - 1).Trim();
                    arguments = line.Substring(closingQuoteIndex + 1).Trim();
                    return !string.IsNullOrWhiteSpace(command);
                }
            }

            var separatorIndex = line.IndexOf(' ');
            if (separatorIndex < 0)
            {
                command = line.Trim('"');
                return !string.IsNullOrWhiteSpace(command);
            }

            command = line.Substring(0, separatorIndex).Trim('"');
            arguments = line.Substring(separatorIndex + 1).Trim();
            return !string.IsNullOrWhiteSpace(command);
        }
    }
}
