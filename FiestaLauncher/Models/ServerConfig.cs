using System.Collections.Generic;

namespace FiestaLauncher.Models
{
    public class ServerConfig
    {
        public string ServerName { get; set; } = "NanOnline";
        public string PatchUrl { get; set; } = "";
        public string PatchListUrl { get; set; } = "";
        public string PatchDownloadUrlTemplate { get; set; } = "";
        public string LoginApiUrl { get; set; } = "";
        public string LauncherLoginUrl { get; set; } = "";
        public string LauncherStartUrl { get; set; } = "";
        public string LauncherHeartbeatUrl { get; set; } = "";
        public string LauncherSecurityEventUrl { get; set; } = "";
        public string OskStoreUrl { get; set; } = "";
        public string WebsiteUrl { get; set; } = "";
        public string RegisterUrl { get; set; } = "";
        public string ForgotPasswordUrl { get; set; } = "";
        public string GameExecutable { get; set; } = "Nano.exe";
        public string WrapperExecutable { get; set; } = "Nano.exe";
        public string GameBinaryExecutable { get; set; } = "Nano.bin";
        public string SecurityTrayExecutable { get; set; } = "NanoSecurityTray.exe";
        public string GameArguments { get; set; } = "";
        public string GameDirectory { get; set; } = "";
        public string Version { get; set; } = "1.0.0";
        public string DatabaseConnectionString { get; set; } = "";
        public bool UseApiLogin { get; set; } = true;
        public List<NewsItem> News { get; set; } = new();

        public string ResolveWrapperExecutable()
        {
            if (!string.IsNullOrWhiteSpace(WrapperExecutable))
            {
                return WrapperExecutable;
            }

            return GameExecutable;
        }
    }

    public class NewsItem
    {
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
        public string Date { get; set; } = "";
    }
}
