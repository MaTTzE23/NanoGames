using System.Collections.Generic;
using Newtonsoft.Json;

namespace FiestaLauncher.Models
{
    public class PatchList
    {
        public string CurrentVersion { get; set; } = "";
        public string EntryExecutable { get; set; } = "";
        public List<string> CleanupPaths { get; set; } = new();
        public List<PatchFileInfo> Files { get; set; } = new();
    }

    public class PatchFileInfo
    {
        public string FileName { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public string MD5Hash { get; set; } = "";
        public string SHA256Hash { get; set; } = "";
        public long FileSize { get; set; }
        public string DownloadUrl { get; set; } = "";
        public string Version { get; set; } = "";
        public bool ForceUpdate { get; set; } = false;

        public string ExpectedHash => SHA256Hash;
        public bool HasValidSha256 => !string.IsNullOrWhiteSpace(SHA256Hash);
    }

    public class PatchProgress
    {
        public string CurrentFile { get; set; } = "";
        public int TotalFiles { get; set; }
        public int CompletedFiles { get; set; }
        public double DownloadProgress { get; set; }
        public long BytesDownloaded { get; set; }
        public long TotalBytes { get; set; }
        public string Status { get; set; } = "";
        public double OverallProgress => TotalFiles > 0 ? (double)CompletedFiles / TotalFiles * 100 : 0;
    }

    public class ModernPatchManifest
    {
        [JsonProperty("project")]
        public string Project { get; set; } = "";

        [JsonProperty("channel")]
        public string Channel { get; set; } = "";

        [JsonProperty("version")]
        public string Version { get; set; } = "";

        [JsonProperty("minimum_launcher_version")]
        public string MinimumLauncherVersion { get; set; } = "";

        [JsonProperty("entry_executable")]
        public string EntryExecutable { get; set; } = "";

        [JsonProperty("cleanup_paths")]
        public List<string> CleanupPaths { get; set; } = new();

        [JsonProperty("files")]
        public List<ModernPatchFile> Files { get; set; } = new();
    }

    public class ModernPatchFile
    {
        [JsonProperty("path")]
        public string Path { get; set; } = "";

        [JsonProperty("sha256")]
        public string Sha256 { get; set; } = "";

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; } = "";
    }
}
