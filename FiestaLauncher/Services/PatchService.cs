using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FiestaLauncher.Models;
using Newtonsoft.Json;

namespace FiestaLauncher.Services
{
    public class PatchService : IDisposable
    {
        private static readonly string[] ObsoleteRootFiles =
        {
            "Game.bat",
            "Fiesta.bin",
            "launcher_settings.dat"
        };

        private readonly HttpClient _httpClient;
        private readonly ServerConfig _config;
        private readonly string _currentExecutablePath = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? string.Empty;
        private readonly List<string> _manifestCleanupPaths = new();
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly List<PendingReplacementFile> _pendingLauncherFileReplacements = new();

        private static readonly HashSet<string> RestartManagedFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "NanOnlineLauncher.exe",
            "NanOnlineLauncher.dll",
            "NanOnlineLauncher.deps.json",
            "NanOnlineLauncher.runtimeconfig.json",
            "FiestaLauncher.exe",
            "D3DCompiler_47_cor3.dll",
            "Newtonsoft.Json.dll",
            "PenImc_cor3.dll",
            "PresentationNative_cor3.dll",
            "sni.dll",
            "System.Data.SqlClient.dll",
            "vcruntime140_cor3.dll",
            "wpfgfx_cor3.dll",
            "server.json"
        };

        public event Action<PatchProgress>? OnProgressChanged;
        public event Action<string>? OnLogMessage;
        public event Action<bool, string>? OnPatchCompleted;

        public PatchService(ServerConfig config)
        {
            _config = config;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(30)
            };
        }

        public async Task<PatchList?> GetPatchListAsync()
        {
            try
            {
                OnLogMessage?.Invoke("Downloading patch manifest...");
                var response = await _httpClient.GetStringAsync(_config.PatchListUrl);
                var patchList = ParsePatchList(response);
                OnLogMessage?.Invoke(string.Format("Patch manifest loaded. Server version: {0}", patchList?.CurrentVersion));
                return patchList;
            }
            catch (Exception ex)
            {
                OnLogMessage?.Invoke(string.Format("Failed to load patch manifest: {0}", ex.Message));
                return null;
            }
        }

        public async Task<List<PatchFileInfo>> CheckForUpdatesAsync(PatchList patchList)
        {
            var filesToUpdate = new List<PatchFileInfo>();
            var gameDir = GetGameDirectory();
            _manifestCleanupPaths.Clear();
            _manifestCleanupPaths.AddRange(
                patchList.CleanupPaths
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase));

            OnLogMessage?.Invoke("Checking local files for updates...");

            var progress = new PatchProgress
            {
                TotalFiles = patchList.Files.Count,
                Status = "Checking files..."
            };

            for (int i = 0; i < patchList.Files.Count; i++)
            {
                var file = patchList.Files[i];
                var localPath = Path.Combine(gameDir, file.RelativePath, file.FileName);

                progress.CurrentFile = file.FileName;
                progress.CompletedFiles = i;
                OnProgressChanged?.Invoke(progress);

                bool needsUpdate = false;

                if (!File.Exists(localPath))
                {
                    needsUpdate = true;
                    OnLogMessage?.Invoke(string.Format("  Missing locally: {0}", file.FileName));
                }
                else if (file.ForceUpdate)
                {
                    needsUpdate = true;
                    OnLogMessage?.Invoke(string.Format("  Forced update: {0}", file.FileName));
                }
                else if (!file.HasValidSha256)
                {
                    needsUpdate = true;
                    OnLogMessage?.Invoke(string.Format("  Missing SHA256 entry in manifest: {0}", file.FileName));
                }
                else
                {
                    var localHash = await HashService.CalculateSHA256Async(localPath);

                    if (!string.Equals(localHash, file.ExpectedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        needsUpdate = true;
                        OnLogMessage?.Invoke(string.Format("  Outdated: {0}", file.FileName));
                    }
                }

                if (needsUpdate)
                {
                    filesToUpdate.Add(file);
                }
            }

            OnLogMessage?.Invoke(string.Format("{0} file(s) need to be updated.", filesToUpdate.Count));
            return filesToUpdate;
        }

        public async Task StartPatchingAsync(List<PatchFileInfo> filesToUpdate, string? targetVersion = null)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;
            var gameDir = GetGameDirectory();
            _pendingLauncherFileReplacements.Clear();

            var progress = new PatchProgress
            {
                TotalFiles = filesToUpdate.Count,
                Status = "Installing updates..."
            };

            long totalBytes = 0;
            foreach (var file in filesToUpdate)
                totalBytes += file.FileSize;
            progress.TotalBytes = totalBytes;

            try
            {
                for (int i = 0; i < filesToUpdate.Count; i++)
                {
                    token.ThrowIfCancellationRequested();

                    var file = filesToUpdate[i];
                    progress.CurrentFile = file.FileName;
                    progress.CompletedFiles = i;
                    progress.Status = string.Format("Downloading: {0}", file.FileName);
                    OnProgressChanged?.Invoke(progress);

                    await DownloadFileAsync(file, gameDir, progress, token);

                    OnLogMessage?.Invoke(string.Format("  OK: downloaded {0}", file.FileName));
                }

                progress.CompletedFiles = filesToUpdate.Count;
                progress.Status = "Update complete.";
                progress.DownloadProgress = 100;
                OnProgressChanged?.Invoke(progress);

                SaveLocalVersion(string.IsNullOrWhiteSpace(targetVersion) ? _config.Version : targetVersion);
                CleanupClientArtifacts(gameDir);

                if (_pendingLauncherFileReplacements.Count > 0)
                {
                    OnLogMessage?.Invoke("Launcher runtime files were updated. Restarting launcher to complete replacement...");
                    OnPatchCompleted?.Invoke(true, "Launcher updated. Restarting...");
                    SchedulePendingLauncherRestart();
                    return;
                }

                OnPatchCompleted?.Invoke(true, "All files were updated successfully.");
                OnLogMessage?.Invoke("=== Update finished successfully ===");
            }
            catch (OperationCanceledException)
            {
                OnPatchCompleted?.Invoke(false, "Update was cancelled.");
                OnLogMessage?.Invoke("Update was cancelled by the user.");
            }
            catch (Exception ex)
            {
                OnPatchCompleted?.Invoke(false, string.Format("Update failed: {0}", ex.Message));
                OnLogMessage?.Invoke(string.Format("ERROR: {0}", ex.Message));
            }
        }

        private async Task DownloadFileAsync(PatchFileInfo file, string gameDir,
            PatchProgress progress, CancellationToken token)
        {
            var downloadUrl = string.IsNullOrEmpty(file.DownloadUrl)
                ? string.Format("{0}{1}/{2}", _config.PatchUrl, file.RelativePath, file.FileName).Replace("\\", "/")
                : file.DownloadUrl;

            var localDir = Path.Combine(gameDir, file.RelativePath);
            Directory.CreateDirectory(localDir);

            var localPath = Path.Combine(localDir, file.FileName);
            var tempPath = localPath + ".tmp";
            var backupPath = localPath + ".bak";
            var originalMoved = false;

            try
            {
                using var response = await _httpClient.GetAsync(downloadUrl,
                    HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();

                var totalFileBytes = response.Content.Headers.ContentLength ?? file.FileSize;

                using var contentStream = await response.Content.ReadAsStreamAsync(token);
                using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write,
                    FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, token);
                    totalRead += bytesRead;
                    progress.BytesDownloaded += bytesRead;

                    if (totalFileBytes > 0)
                    {
                        progress.DownloadProgress = (double)totalRead / totalFileBytes * 100;
                    }

                    OnProgressChanged?.Invoke(progress);
                }

                fileStream.Close();

                // Verify integrity before replacing the local file.
                if (!file.HasValidSha256)
                {
                    File.Delete(tempPath);
                    throw new Exception(string.Format("Manifest is missing SHA256 for {0}", file.FileName));
                }

                var downloadedHash = await HashService.CalculateSHA256Async(tempPath);

                if (!string.IsNullOrEmpty(file.ExpectedHash) &&
                    !string.Equals(downloadedHash, file.ExpectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(tempPath);
                    throw new Exception(string.Format("Hash verification failed for {0}", file.FileName));
                }

                if (RequiresRestartForReplacement(localPath))
                {
                    var stagedPath = localPath + ".next";
                    if (File.Exists(stagedPath))
                        File.Delete(stagedPath);

                    File.Move(tempPath, stagedPath);
                    QueuePendingReplacement(localPath, stagedPath);
                    OnLogMessage?.Invoke(string.Format("  Staged for restart: {0}", file.FileName));
                    return;
                }

                // Replace the original file only after the temporary download is valid.
                if (File.Exists(localPath))
                {
                    if (File.Exists(backupPath))
                        File.Delete(backupPath);
                    File.Move(localPath, backupPath);
                    originalMoved = true;
                }

                File.Move(tempPath, localPath);

                // Remove the backup after a successful swap.
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
            }
            catch
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);

                if (originalMoved && !File.Exists(localPath) && File.Exists(backupPath))
                    File.Move(backupPath, localPath);

                throw;
            }
        }

        public void CancelPatching()
        {
            _cancellationTokenSource?.Cancel();
        }

        private string GetGameDirectory()
        {
            if (!string.IsNullOrEmpty(_config.GameDirectory))
                return _config.GameDirectory;
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        public string? GetLocalVersion()
        {
            var versionFile = Path.Combine(GetGameDirectory(), "version.dat");
            if (File.Exists(versionFile))
                return File.ReadAllText(versionFile).Trim();
            return null;
        }

        private void SaveLocalVersion(string version)
        {
            var versionFile = Path.Combine(GetGameDirectory(), "version.dat");
            File.WriteAllText(versionFile, version);
        }

        public void CleanupClientArtifacts()
        {
            CleanupClientArtifacts(GetGameDirectory());
        }

        private void CleanupClientArtifacts(string gameDir)
        {
            DeleteMatchingFiles(gameDir, "*.zip", SearchOption.TopDirectoryOnly);
            DeleteMatchingFiles(gameDir, "*.rar", SearchOption.TopDirectoryOnly);
            DeleteMatchingFiles(gameDir, "*.tmp", SearchOption.AllDirectories);
            DeleteMatchingFiles(gameDir, "*.bak", SearchOption.AllDirectories);

            foreach (var relativeFile in ObsoleteRootFiles)
            {
                var fullPath = Path.Combine(gameDir, relativeFile);
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                try
                {
                    File.Delete(fullPath);
                    OnLogMessage?.Invoke(string.Format("Removed obsolete file: {0}", relativeFile));
                }
                catch (Exception ex)
                {
                    OnLogMessage?.Invoke(string.Format("Failed to remove obsolete file {0}: {1}", relativeFile, ex.Message));
                }
            }

            foreach (var relativePath in _manifestCleanupPaths)
            {
                DeleteManifestCleanupPath(gameDir, relativePath);
            }
        }

        private void DeleteMatchingFiles(string rootPath, string searchPattern, SearchOption searchOption)
        {
            IEnumerable<string> files;

            try
            {
                files = Directory.EnumerateFiles(rootPath, searchPattern, searchOption);
            }
            catch (Exception ex)
            {
                OnLogMessage?.Invoke(string.Format("Cleanup scan failed for {0}: {1}", searchPattern, ex.Message));
                return;
            }

            foreach (var file in files)
            {
                try
                {
                    File.Delete(file);
                    OnLogMessage?.Invoke(string.Format("Removed temporary file: {0}", Path.GetFileName(file)));
                }
                catch (Exception ex)
                {
                    OnLogMessage?.Invoke(string.Format("Failed to remove temporary file {0}: {1}", Path.GetFileName(file), ex.Message));
                }
            }
        }

        private void DeleteManifestCleanupPath(string gameDir, string relativePath)
        {
            var fullPath = Path.Combine(gameDir, relativePath);

            try
            {
                if (Directory.Exists(fullPath))
                {
                    Directory.Delete(fullPath, true);
                    OnLogMessage?.Invoke(string.Format("Removed obsolete directory: {0}", relativePath));
                    return;
                }

                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    OnLogMessage?.Invoke(string.Format("Removed obsolete file: {0}", relativePath));
                }
            }
            catch (Exception ex)
            {
                OnLogMessage?.Invoke(string.Format("Failed to remove obsolete path {0}: {1}", relativePath, ex.Message));
            }
        }

        public void Dispose()
        {
            _httpClient.Dispose();
            _cancellationTokenSource?.Dispose();
        }

        private bool RequiresRestartForReplacement(string localPath)
        {
            if (string.IsNullOrWhiteSpace(localPath))
                return false;

            var normalizedLocalPath = Path.GetFullPath(localPath);
            if (string.Equals(normalizedLocalPath, Path.GetFullPath(_currentExecutablePath), StringComparison.OrdinalIgnoreCase))
                return true;

            var fileName = Path.GetFileName(localPath);
            return RestartManagedFiles.Contains(fileName);
        }

        private void QueuePendingReplacement(string targetPath, string stagedPath)
        {
            _pendingLauncherFileReplacements.RemoveAll(item =>
                string.Equals(item.TargetPath, targetPath, StringComparison.OrdinalIgnoreCase));

            _pendingLauncherFileReplacements.Add(new PendingReplacementFile(targetPath, stagedPath));
        }

        private void SchedulePendingLauncherRestart()
        {
            if (_pendingLauncherFileReplacements.Count == 0)
                return;

            var scriptPath = Path.Combine(
                Path.GetTempPath(),
                "nanonline-launcher-update-" + Guid.NewGuid().ToString("N") + ".cmd");

            var restartTarget = _pendingLauncherFileReplacements
                .Select(item => item.TargetPath)
                .FirstOrDefault(path =>
                    string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
                ?? _currentExecutablePath;

            var script = BuildReplacementScript(_pendingLauncherFileReplacements, restartTarget);
            File.WriteAllText(scriptPath, script, Encoding.ASCII);

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c \"" + scriptPath + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(restartTarget) ?? GetGameDirectory()
            });

            Environment.Exit(0);
        }

        private static string BuildReplacementScript(
            IReadOnlyList<PendingReplacementFile> replacements,
            string restartTarget)
        {
            var script = new StringBuilder();
            script.AppendLine("@echo off");
            script.AppendLine("setlocal enableextensions");
            script.AppendLine();
            script.AppendLine(":replace");
            script.AppendLine("set \"FAILED=0\"");

            foreach (var replacement in replacements)
            {
                script.AppendLine("copy /y \"" + replacement.StagedPath + "\" \"" + replacement.TargetPath + "\" >nul 2>nul || set \"FAILED=1\"");
            }

            script.AppendLine("if \"%FAILED%\"==\"1\" (");
            script.AppendLine("    timeout /t 1 /nobreak >nul");
            script.AppendLine("    goto replace");
            script.AppendLine(")");
            script.AppendLine();
            script.AppendLine("start \"\" \"" + restartTarget + "\"");

            foreach (var replacement in replacements)
            {
                script.AppendLine("del /f /q \"" + replacement.StagedPath + "\" >nul 2>nul");
            }

            script.AppendLine("del /f /q \"%~f0\" >nul 2>nul");
            return script.ToString();
        }

        private PatchList? ParsePatchList(string rawJson)
        {
            var legacyPatchList = JsonConvert.DeserializeObject<PatchList>(rawJson);
            if (LooksLikeLegacyPatchList(legacyPatchList))
                return legacyPatchList;

            var modernManifest = JsonConvert.DeserializeObject<ModernPatchManifest>(rawJson);
            if (modernManifest?.Files == null || modernManifest.Files.Count == 0)
                return legacyPatchList;

            return new PatchList
            {
                CurrentVersion = modernManifest.Version,
                EntryExecutable = modernManifest.EntryExecutable,
                CleanupPaths = modernManifest.CleanupPaths ?? new List<string>(),
                Files = modernManifest.Files.Select(ConvertModernFile).ToList()
            };
        }

        private PatchFileInfo ConvertModernFile(ModernPatchFile file)
        {
            var normalizedPath = (file.Path ?? string.Empty).Replace('/', Path.DirectorySeparatorChar);
            var fileName = Path.GetFileName(normalizedPath);
            var directory = Path.GetDirectoryName(normalizedPath) ?? string.Empty;

            return new PatchFileInfo
            {
                FileName = fileName,
                RelativePath = directory,
                SHA256Hash = file.Sha256 ?? string.Empty,
                FileSize = file.Size,
                DownloadUrl = ResolveDownloadUrl(file),
                Version = string.Empty,
                ForceUpdate = false
            };
        }

        private string ResolveDownloadUrl(ModernPatchFile file)
        {
            var relativeFile = string.IsNullOrWhiteSpace(file.Url) ? file.Path : file.Url;
            var normalizedRelativeFile = (relativeFile ?? string.Empty).Replace("\\", "/").TrimStart('/');

            if (Uri.TryCreate(normalizedRelativeFile, UriKind.Absolute, out var absoluteUri))
                return absoluteUri.ToString();

            var template = (_config.PatchDownloadUrlTemplate ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(template))
                return ApplyDownloadTemplate(template, normalizedRelativeFile);

            var baseUrl = (_config.PatchUrl ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                if (baseUrl.Contains("{file}", StringComparison.OrdinalIgnoreCase) ||
                    baseUrl.Contains("{path}", StringComparison.OrdinalIgnoreCase))
                {
                    return ApplyDownloadTemplate(baseUrl, normalizedRelativeFile);
                }

                if (!baseUrl.EndsWith("/"))
                    baseUrl += "/";

                return new Uri(new Uri(baseUrl), normalizedRelativeFile).ToString();
            }

            if (Uri.TryCreate(_config.PatchListUrl, UriKind.Absolute, out var manifestUri))
                return new Uri(manifestUri, normalizedRelativeFile).ToString();

            return normalizedRelativeFile;
        }

        private static string ApplyDownloadTemplate(string template, string relativeFile)
        {
            var encodedPath = Uri.EscapeDataString(relativeFile);
            return template
                .Replace("{file}", encodedPath, StringComparison.OrdinalIgnoreCase)
                .Replace("{path}", encodedPath, StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeLegacyPatchList(PatchList? patchList)
        {
            if (patchList?.Files == null || patchList.Files.Count == 0)
                return false;

            return patchList.Files.Any(file =>
                !string.IsNullOrWhiteSpace(file.FileName) ||
                !string.IsNullOrWhiteSpace(file.MD5Hash) ||
                !string.IsNullOrWhiteSpace(file.RelativePath));
        }

        private sealed record PendingReplacementFile(string TargetPath, string StagedPath);
    }
}
