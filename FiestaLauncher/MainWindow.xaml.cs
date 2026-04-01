using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FiestaLauncher.Models;
using FiestaLauncher.Services;
using FiestaLauncher.Shared.Models;
using FiestaLauncher.Shared.Security;
using Newtonsoft.Json;

namespace FiestaLauncher
{
    public partial class MainWindow : Window
    {
        private ServerConfig _config = new();
        private PatchService? _patchService;
        private GameLauncherService? _gameLauncher;
        private LoginService? _loginService;
        private WrapperLaunchService? _wrapperLaunchService;
        private SecurityTrayClient? _securityTrayClient;

        private readonly List<PatchFileInfo> _pendingUpdates = new();
        private readonly string _machineId = HashService.CalculateSHA256String(
            string.Format(
                "{0}|{1}|{2}",
                Environment.MachineName,
                Environment.UserName,
                Environment.OSVersion.VersionString));

        private PatchList? _currentPatchList;
        private LauncherLoginResponse? _launcherSession;
        private LauncherSessionState _sessionState = LauncherSessionState.LoggedOut;
        private bool _isPatching;
        private bool _isPatchRequired;
        private bool _patchServerReachable;
        private bool _patchCheckFailed;
        private bool _isPasswordVisible;
        private bool _isSyncingPassword;
        private string? _targetPatchVersion;
        private string? _loggedInUsername;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadConfig();
            InitializeServices();
            PopulateNews();
            SetPasswordVisibility(false);
            UpdatePasswordWatermark();
            ApplyAuthenticationUiState();
            UpdateSessionStatusAfterPatch();
            await CheckForUpdatesAsync();
        }

        private void LoadConfig()
        {
            try
            {
                var configPath = GetConfigPath();

                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    _config = JsonConvert.DeserializeObject<ServerConfig>(json) ?? new ServerConfig();
                }
                else
                {
                    _config = new ServerConfig();
                    File.WriteAllText(configPath, JsonConvert.SerializeObject(_config, Formatting.Indented));
                    AppendLog("A default configuration file was created. Update server.json before using the launcher.");
                }

                NormalizeConfig();

                Title = string.Format("{0} Launcher", _config.ServerName);
                txtServerName.Text = _config.ServerName;
                txtVersion.Text = string.Format("Version: {0}", _config.Version);
                txtPatchVersionInfo.Text = _config.Version;
                txtInlineUsername.Text = _loggedInUsername ?? string.Empty;
            }
            catch (Exception ex)
            {
                AppendLog(string.Format("Failed to load launcher configuration: {0}", ex.Message));
                _config = new ServerConfig();
                NormalizeConfig();
                txtServerName.Text = _config.ServerName;
                txtVersion.Text = string.Format("Version: {0}", _config.Version);
                txtPatchVersionInfo.Text = _config.Version;
            }
        }

        private void NormalizeConfig()
        {
            if (string.IsNullOrWhiteSpace(_config.ServerName))
            {
                _config.ServerName = "NanOnline";
            }

            _config.PatchUrl = string.IsNullOrWhiteSpace(_config.PatchUrl)
                ? "https://patch.nanonline.net/"
                : _config.PatchUrl;
            _config.PatchListUrl = string.IsNullOrWhiteSpace(_config.PatchListUrl)
                ? "https://patch.nanonline.net/api/patch_manifest.php"
                : _config.PatchListUrl;
            _config.PatchDownloadUrlTemplate = string.IsNullOrWhiteSpace(_config.PatchDownloadUrlTemplate)
                ? "https://patch.nanonline.net/api/patch_file.php?path={file}"
                : _config.PatchDownloadUrlTemplate;
            _config.LoginApiUrl = string.IsNullOrWhiteSpace(_config.LoginApiUrl)
                ? "https://nanonline.net/api/auth/login.php"
                : _config.LoginApiUrl;
            _config.LauncherLoginUrl = string.IsNullOrWhiteSpace(_config.LauncherLoginUrl)
                ? "https://nanonline.net/api/launcher/login.php"
                : _config.LauncherLoginUrl;
            _config.LauncherStartUrl = string.IsNullOrWhiteSpace(_config.LauncherStartUrl)
                ? "https://nanonline.net/api/launcher/start.php"
                : _config.LauncherStartUrl;
            _config.LauncherHeartbeatUrl = string.IsNullOrWhiteSpace(_config.LauncherHeartbeatUrl)
                ? "https://nanonline.net/api/launcher/heartbeat.php"
                : _config.LauncherHeartbeatUrl;
            _config.LauncherSecurityEventUrl = string.IsNullOrWhiteSpace(_config.LauncherSecurityEventUrl)
                ? "https://nanonline.net/api/launcher/security_event.php"
                : _config.LauncherSecurityEventUrl;
            _config.OskStoreUrl = string.IsNullOrWhiteSpace(_config.OskStoreUrl)
                ? "https://nanonline.net/"
                : _config.OskStoreUrl;
            _config.WebsiteUrl = string.IsNullOrWhiteSpace(_config.WebsiteUrl)
                ? "https://nanonline.net/"
                : _config.WebsiteUrl;
            _config.RegisterUrl = string.IsNullOrWhiteSpace(_config.RegisterUrl)
                ? "https://nanonline.net/register.php"
                : _config.RegisterUrl;
            _config.ForgotPasswordUrl = string.IsNullOrWhiteSpace(_config.ForgotPasswordUrl)
                ? "https://nanonline.net/forgot_password.php"
                : _config.ForgotPasswordUrl;
            _config.WrapperExecutable = string.IsNullOrWhiteSpace(_config.WrapperExecutable)
                ? "Nano.exe"
                : _config.WrapperExecutable;
            _config.GameBinaryExecutable = string.IsNullOrWhiteSpace(_config.GameBinaryExecutable)
                ? "Nano.bin"
                : _config.GameBinaryExecutable;
            _config.SecurityTrayExecutable = string.IsNullOrWhiteSpace(_config.SecurityTrayExecutable)
                ? "NanoSecurityTray.exe"
                : _config.SecurityTrayExecutable;
            _config.Version = string.IsNullOrWhiteSpace(_config.Version)
                ? "1.3.1"
                : _config.Version;
            _config.UseApiLogin = true;
        }

        private void InitializeServices()
        {
            _patchService = new PatchService(_config);
            _patchService.OnProgressChanged += PatchService_OnProgressChanged;
            _patchService.OnLogMessage += PatchService_OnLogMessage;
            _patchService.OnPatchCompleted += PatchService_OnPatchCompleted;

            _gameLauncher = new GameLauncherService(_config);
            _loginService = new LoginService(_config);
            _wrapperLaunchService = new WrapperLaunchService();
            _securityTrayClient = new SecurityTrayClient();
        }

        private void PopulateNews()
        {
            newsPanel.Children.Clear();

            var newsItems = new List<NewsItem>
            {
                new NewsItem
                {
                    Title = "Welcome NanOnline",
                    Content = "Install the latest updates and jump straight into the game.",
                    Date = DateTime.Now.ToString("yyyy-MM-dd")
                },
                new NewsItem
                {
                    Title = "Patch 1.3.1 live",
                    Content = "New content and balance fixes are now available.",
                    Date = DateTime.Now.ToString("yyyy-MM-dd")
                }
            };

            foreach (var news in newsItems)
            {
                AddNewsItem(news.Title, news.Content, news.Date);
            }
        }

        private void AddNewsItem(string title, string content, string date)
        {
            var row = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 18)
            };

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleStack = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            var dot = new Border
            {
                Width = 10,
                Height = 10,
                Background = (SolidColorBrush)FindResource("SuccessBrush"),
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(0, 6, 12, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            titleStack.Children.Add(dot);

            var titleBlock = new TextBlock
            {
                Text = title,
                Foreground = (SolidColorBrush) FindResource("TextBrush"),
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap
            };
            titleStack.Children.Add(titleBlock);
            Grid.SetColumn(titleStack, 0);
            headerGrid.Children.Add(titleStack);

            var dateBlock = new TextBlock
            {
                Text = date,
                Foreground = (SolidColorBrush) FindResource("SubTextBrush"),
                FontSize = 12,
                Margin = new Thickness(14, 2, 0, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(dateBlock, 1);
            headerGrid.Children.Add(dateBlock);

            row.Children.Add(headerGrid);

            var contentBlock = new TextBlock
            {
                Text = content,
                Foreground = (SolidColorBrush) FindResource("SubTextBrush"),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(22, 8, 0, 0)
            };
            row.Children.Add(contentBlock);
            newsPanel.Children.Add(row);
        }

        private async Task CheckForUpdatesAsync()
        {
            if (_patchService == null)
            {
                return;
            }

            try
            {
                if (_securityTrayClient != null)
                {
                    await _securityTrayClient.TryShutdownAsync(GetTrayProcessName());
                }

                _patchCheckFailed = false;
                btnCheckUpdate.IsEnabled = false;
                btnCheckUpdate.Content = "CHECKING...";
                btnStartGame.IsEnabled = false;

                SetPatchStatus("Checking patch manifest...", false);
                SetActionStatus(string.Empty, false);
                SetPatchProgressText("Verifying the current client version...");

                _currentPatchList = await _patchService.GetPatchListAsync();

                if (_currentPatchList == null)
                {
                    _pendingUpdates.Clear();
                    _isPatchRequired = false;
                    _patchCheckFailed = true;
                    SetConnectionStatus(false);
                    SetPatchStatus("Patch service unavailable", true);
                    SetActionStatus("Patchserver unreachable.", true);
                    SetPatchProgressText("Try again in a moment.");
                    RefreshPrimaryActionButton();
                    return;
                }

                SetConnectionStatus(true);
                _targetPatchVersion = _currentPatchList.CurrentVersion;

                if (!string.IsNullOrWhiteSpace(_currentPatchList.EntryExecutable))
                {
                    _config.GameExecutable = _currentPatchList.EntryExecutable;
                }

                if (!string.IsNullOrWhiteSpace(_currentPatchList.CurrentVersion))
                {
                    txtVersion.Text = string.Format("Version: {0}", _currentPatchList.CurrentVersion);
                    txtPatchVersionInfo.Text = _currentPatchList.CurrentVersion;
                }

                var filesToUpdate = await _patchService.CheckForUpdatesAsync(_currentPatchList);

                _pendingUpdates.Clear();
                _pendingUpdates.AddRange(filesToUpdate);

                if (filesToUpdate.Count > 0)
                {
                    _isPatchRequired = true;
                    SetPatchStatus(string.Format("{0} update(s) available", filesToUpdate.Count), false);
                    SetActionStatus("Installing update...", false);
                    RefreshPrimaryActionButton();
                    await StartPatchingAsync(new List<PatchFileInfo>(_pendingUpdates));
                    return;
                }

                _isPatchRequired = false;
                _patchService.CleanupClientArtifacts();
                SetPatchStatus("Client ready", false);
                SetPatchProgressText("Ready to launch.");
                txtPatchVersionInfo.Text = _targetPatchVersion ?? _config.Version;
                UpdateSessionStatusAfterPatch();
                RefreshPrimaryActionButton();
            }
            catch (Exception ex)
            {
                AppendLog(string.Format("Update check failed: {0}", ex.Message));
                _patchCheckFailed = true;
                SetConnectionStatus(false);
                SetPatchStatus("Update check failed", true);
                SetActionStatus("Patchserver unreachable.", true);
                SetPatchProgressText("Please try the update check again.");
                RefreshPrimaryActionButton();
            }
            finally
            {
                if (!_isPatching)
                {
                    btnCheckUpdate.Content = "CHECK UPDATES";
                    btnCheckUpdate.IsEnabled = true;
                }
            }
        }

        private async Task StartPatchingAsync(List<PatchFileInfo> filesToUpdate)
        {
            if (_patchService == null)
            {
                return;
            }

            _isPatching = true;
            progressBar.Value = 0;

            SetPatchStatus("Installing updates...", false);
            SetActionStatus("Downloading files...", false);
            SetPatchProgressText("Preparing the update package...");
            RefreshPrimaryActionButton();

            btnCheckUpdate.IsEnabled = true;
            btnCheckUpdate.Content = "CANCEL";

            await _patchService.StartPatchingAsync(filesToUpdate, _targetPatchVersion);
        }

        private void PatchService_OnProgressChanged(PatchProgress progress)
        {
            Dispatcher.Invoke(() =>
            {
                progressBar.Value = progress.OverallProgress;
                txtPatchStatus.Text = progress.Status;
                txtPatchProgress.Text = string.Format(
                    "{0}/{1} files installed, {2} transferred",
                    progress.CompletedFiles,
                    progress.TotalFiles,
                    FormatBytes(progress.BytesDownloaded));
            });
        }

        private void PatchService_OnLogMessage(string message)
        {
            Dispatcher.Invoke(() => AppendLog(message));
        }

        private void PatchService_OnPatchCompleted(bool success, string message)
        {
            Dispatcher.Invoke(() =>
            {
                _isPatching = false;
                btnCheckUpdate.Content = "CHECK UPDATES";
                btnCheckUpdate.IsEnabled = true;

                if (success)
                {
                    _pendingUpdates.Clear();
                    _isPatchRequired = false;
                    SetPatchStatus(message, false);
                    SetPatchProgressText("Ready to launch.");
                    txtPatchVersionInfo.Text = _targetPatchVersion ?? _config.Version;
                    UpdateSessionStatusAfterPatch();

                    if (chkAutoLaunch.IsChecked == true)
                    {
                        _ = LaunchGameAsync();
                    }
                }
                else
                {
                    _patchCheckFailed = true;
                    SetPatchStatus(message, true);
                    SetActionStatus("Update failed.", true);
                    SetPatchProgressText("Retry the update to finish the client setup.");
                }

                RefreshPrimaryActionButton();
            });
        }

        private async void BtnStartGame_Click(object sender, RoutedEventArgs e)
        {
            if (_isPatching)
            {
                return;
            }

            if (_patchCheckFailed || !_patchServerReachable || _currentPatchList == null)
            {
                await CheckForUpdatesAsync();
                return;
            }

            if (_gameLauncher?.IsGameRunning() == true)
            {
                SetActionStatus("The game is already running.", true);
                return;
            }

            if (_isPatchRequired)
            {
                if (_pendingUpdates.Count == 0)
                {
                    await CheckForUpdatesAsync();
                }

                if (_pendingUpdates.Count == 0)
                {
                    SetActionStatus("No update package is queued. Run another update check.", true);
                    return;
                }

                btnStartGame.Content = "PATCHING...";
                await StartPatchingAsync(new List<PatchFileInfo>(_pendingUpdates));
                return;
            }

            if (!HasLauncherSession())
            {
                await AttemptInlineLoginAsync();
                return;
            }

            await LaunchGameAsync();
        }

        private async Task LaunchGameAsync()
        {
            if (_gameLauncher == null || _loginService == null || _wrapperLaunchService == null || _securityTrayClient == null)
            {
                return;
            }

            btnStartGame.IsEnabled = false;
            btnStartGame.Content = "STARTING...";

            try
            {
                AppendLog("Launch requested through launcher.");
                if (!await EnsureSessionAsync())
                {
                    RefreshPrimaryActionButton();
                    return;
                }

                UpdateSessionState(LauncherSessionState.Starting, "Requesting launcher start session...", false);
                SetPatchProgressText("Requesting a fresh start token...");
                AppendLog("Requesting launcher start session.");

                var startResponse = await _loginService.RequestStartAsync(
                    _launcherSession?.AccessToken ?? string.Empty,
                    _machineId,
                    GetLauncherVersion());

                if (!startResponse.Success)
                {
                    AppendLog("Launcher start session failed: " + startResponse.Message);
                    ClearSession();
                    RestoreReadyState(startResponse.Message, true);
                    return;
                }

                AppendLog("Launcher start session created successfully.");

                var clientRoot = ResolveGameDirectory();
                var trayResult = await _securityTrayClient.EnsureSessionAsync(
                    _config,
                    _launcherSession?.AccessToken ?? string.Empty,
                    startResponse.StartToken,
                    BuildExpectedHashes(),
                    clientRoot,
                    Path.GetFileNameWithoutExtension(_config.GameBinaryExecutable));

                if (!trayResult.Success)
                {
                    await _securityTrayClient.TryShutdownAsync(GetTrayProcessName());
                    AppendLog("Security tray failed: " + trayResult.Message);
                    RestoreReadyState("The security service could not be started.", true);
                    return;
                }

                UpdateSessionState(LauncherSessionState.TrayConnected, "Security service active. Starting game...", false);

                var wrapperPayload = new WrapperLaunchPayload
                {
                    LaunchId = Guid.NewGuid().ToString("N"),
                    LoginHost = startResponse.LoginHost,
                    LoginPort = startResponse.LoginPort,
                    OskServer = startResponse.OskServer,
                    OskStore = !string.IsNullOrWhiteSpace(startResponse.OskStore)
                        ? startResponse.OskStore
                        : (!string.IsNullOrWhiteSpace(_config.OskStoreUrl) ? _config.OskStoreUrl : _config.RegisterUrl),
                    StartToken = startResponse.StartToken,
                    ClientRoot = clientRoot,
                    GameBinary = _config.GameBinaryExecutable
                };

                var wrapperPath = Path.Combine(clientRoot, _config.ResolveWrapperExecutable());
                var launchResult = await _wrapperLaunchService.LaunchAsync(wrapperPath, clientRoot, wrapperPayload);

                if (!launchResult.Success)
                {
                    await _securityTrayClient.TryShutdownAsync(GetTrayProcessName());
                    AppendLog(launchResult.Message);
                    RestoreReadyState("The game bootstrapper could not be started.", true);
                    return;
                }

                AppendLog(launchResult.Message);
                SetPatchStatus("Starting game client...", false);
                SetPatchProgressText("The game is booting through the local wrapper.");

                var launchCheck = await WaitForGameLaunchAsync(launchResult.Process);
                if (!launchCheck.Success)
                {
                    await _securityTrayClient.TryShutdownAsync(GetTrayProcessName());
                    AppendLog("Launch failed: " + launchCheck.FailureMessage);
                    RestoreReadyState(launchCheck.FailureMessage, true);
                    return;
                }

                UpdateSessionState(LauncherSessionState.GameRunning, "Game launched.", false);
                Close();
            }
            catch (Exception ex)
            {
                await _securityTrayClient.TryShutdownAsync(GetTrayProcessName());
                AppendLog("Unexpected launch failure: " + ex.Message);
                RestoreReadyState("The game could not be started.", true);
            }
        }

        private async Task<bool> EnsureSessionAsync()
        {
            if (HasLauncherSession())
            {
                if (_sessionState != LauncherSessionState.LoggedIn)
                {
                    UpdateSessionState(LauncherSessionState.LoggedIn, string.Empty, false);
                }

                return true;
            }

            return await AttemptInlineLoginAsync();
        }

        private void UpdateSessionStatusAfterPatch()
        {
            if (HasLauncherSession())
            {
                UpdateSessionState(LauncherSessionState.LoggedIn, string.Empty, false);
                return;
            }

            UpdateSessionState(LauncherSessionState.LoggedOut, string.Empty, false);
        }

        private void UpdateSessionState(LauncherSessionState state, string message, bool isError)
        {
            _sessionState = state;
            SetActionStatus(message, isError);
        }

        private void ClearSession()
        {
            _launcherSession = null;
            _loggedInUsername = null;
            ApplyAuthenticationUiState();
        }

        private Dictionary<string, string> BuildExpectedHashes()
        {
            if (_currentPatchList?.Files == null)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var watchedPaths = new HashSet<string>(
                LauncherFileWatchCatalog.RelativePaths
                    .Select(path => path.TrimStart('\\', '/')),
                StringComparer.OrdinalIgnoreCase);

            return _currentPatchList.Files
                .Where(file => file.HasValidSha256)
                .GroupBy(
                    file => Path.Combine(file.RelativePath ?? string.Empty, file.FileName ?? string.Empty)
                        .TrimStart('\\', '/'),
                    StringComparer.OrdinalIgnoreCase)
                .Where(group => watchedPaths.Contains(group.Key))
                .ToDictionary(
                    group => group.Key,
                    group => group.Last().ExpectedHash,
                    StringComparer.OrdinalIgnoreCase);
        }

        private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_isPatching)
            {
                _patchService?.CancelPatching();
                return;
            }

            await CheckForUpdatesAsync();
        }

        private void BtnVisitWebsite_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OpenUrl(_config.WebsiteUrl);
            }
            catch (Exception ex)
            {
                SetActionStatus("The website could not be opened.", true);
                AppendLog(string.Format("Website open failed: {0}", ex.Message));
            }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var configPath = GetConfigPath();

            var result = MessageBox.Show(
                string.Format(
                    "Open the launcher configuration file?\n\n{0}\n\nRestart the launcher after saving changes.",
                    configPath),
                "Launcher Settings",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes && File.Exists(configPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = configPath,
                    UseShellExecute = true
                });
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_isPatching)
            {
                var result = MessageBox.Show(
                    "An update is currently in progress.\nDo you really want to close the launcher?",
                    "Close Launcher",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    return;
                }

                _patchService?.CancelPatching();
            }

            if (_gameLauncher?.IsGameRunning() != true)
            {
                _ = _securityTrayClient?.TryShutdownAsync(GetTrayProcessName());
            }

            base.OnClosing(e);
        }

        private void SetPatchStatus(string text, bool isError)
        {
            txtPatchStatus.Text = text;
            txtPatchStatus.Foreground = isError
                ? (SolidColorBrush) FindResource("ErrorBrush")
                : (SolidColorBrush) FindResource("PrimaryBrush");
        }

        private void SetPatchProgressText(string text)
        {
            txtPatchProgress.Text = text;
        }

        private void SetActionStatus(string text, bool isError)
        {
            txtActionStatus.Text = text;
            txtActionStatus.Foreground = isError
                ? (SolidColorBrush) FindResource("ErrorBrush")
                : (SolidColorBrush) FindResource("SubTextBrush");
            txtActionStatus.Visibility = string.IsNullOrWhiteSpace(text)
                ? Visibility.Collapsed
                : Visibility.Visible;

            RefreshHeroState();
        }

        private void SetConnectionStatus(bool isConnected)
        {
            _patchServerReachable = isConnected;
            statusIndicator.Fill = isConnected
                ? (SolidColorBrush) FindResource("SuccessBrush")
                : (SolidColorBrush) FindResource("WarningBrush");
            txtConnectionStatus.Text = isConnected ? "Online" : "Offline";
            txtConnectionStatus.Foreground = isConnected
                ? (SolidColorBrush) FindResource("SuccessBrush")
                : (SolidColorBrush) FindResource("WarningBrush");
            RefreshHeroState();
        }

        private void RefreshHeroState()
        {
            string title;
            Brush brush;

            if (_isPatching)
            {
                title = "Updating";
                brush = (Brush) FindResource("AccentSoftBrush");
            }
            else if (_currentPatchList == null && !_patchCheckFailed)
            {
                title = "Checking";
                brush = (Brush) FindResource("CyanBrush");
            }
            else if (!_patchServerReachable)
            {
                title = "Offline";
                brush = (Brush) FindResource("WarningBrush");
            }
            else if (_patchCheckFailed)
            {
                title = "Offline";
                brush = (Brush) FindResource("WarningBrush");
            }
            else if (_sessionState == LauncherSessionState.Authenticating)
            {
                title = "Connecting";
                brush = (Brush) FindResource("CyanBrush");
            }
            else if (_sessionState == LauncherSessionState.Starting || _sessionState == LauncherSessionState.TrayConnected)
            {
                title = "Launching";
                brush = (Brush) FindResource("AccentSoftBrush");
            }
            else if (_sessionState == LauncherSessionState.GameRunning)
            {
                title = "Running";
                brush = (Brush) FindResource("SuccessBrush");
            }
            else if (_isPatchRequired)
            {
                title = "Update";
                brush = (Brush) FindResource("AccentSoftBrush");
            }
            else
            {
                title = "Ready";
                brush = (Brush) FindResource("SuccessBrush");
            }

            txtLauncherState.Text = title;
            txtLauncherState.Foreground = brush;
        }

        private void RefreshPrimaryActionButton()
        {
            if (btnStartGame == null)
            {
                return;
            }

            if (_isPatching)
            {
                btnStartGame.Content = "PATCHING...";
                btnStartGame.IsEnabled = false;
                return;
            }

            if (_currentPatchList == null && !_patchCheckFailed)
            {
                btnStartGame.Content = "CHECKING...";
                btnStartGame.IsEnabled = false;
                return;
            }

            if (_patchCheckFailed || !_patchServerReachable)
            {
                btnStartGame.Content = "TRY AGAIN";
                btnStartGame.IsEnabled = true;
                return;
            }

            if (_isPatchRequired)
            {
                btnStartGame.Content = _pendingUpdates.Count > 0 ? "INSTALL UPDATE" : "CHECK UPDATES";
                btnStartGame.IsEnabled = true;
                return;
            }

            btnStartGame.Content = HasLauncherSession() ? "START GAME" : "LOGIN";
            btnStartGame.IsEnabled = true;
        }

        private void AppendLog(string message)
        {
            var sanitizedMessage = LocalLogWriter.Sanitize(message);
            LocalLogWriter.Write("launcher.log", sanitizedMessage, ResolveGameDirectory());

            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            txtLog.AppendText(string.Format("[{0}] {1}\n", timestamp, sanitizedMessage));
            txtLog.ScrollToEnd();
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            var order = 0;
            double size = bytes;

            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }

            return string.Format("{0:0.##} {1}", size, sizes[order]);
        }

        private string GetConfigPath()
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var rootConfigPath = Path.Combine(baseDirectory, "server.json");
            var legacyConfigPath = Path.Combine(baseDirectory, "Config", "server.json");

            if (File.Exists(rootConfigPath))
            {
                return rootConfigPath;
            }

            if (File.Exists(legacyConfigPath))
            {
                File.Copy(legacyConfigPath, rootConfigPath, true);
                AppendLog("Migrated legacy Config/server.json to the client root.");
                return rootConfigPath;
            }

            return rootConfigPath;
        }

        private string ResolveGameDirectory()
        {
            if (!string.IsNullOrWhiteSpace(_config.GameDirectory))
            {
                return _config.GameDirectory;
            }

            return AppDomain.CurrentDomain.BaseDirectory;
        }

        private async Task<(bool Success, string FailureMessage)> WaitForGameLaunchAsync(Process? wrapperProcess)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);

            while (DateTime.UtcNow < deadline)
            {
                if (_gameLauncher?.IsGameRunning() == true)
                {
                    return (true, string.Empty);
                }

                if (wrapperProcess != null)
                {
                    wrapperProcess.Refresh();
                    if (wrapperProcess.HasExited && wrapperProcess.ExitCode != 0)
                    {
                        return (false, GetWrapperExitMessage(wrapperProcess.ExitCode));
                    }
                }

                await Task.Delay(250);
            }

            if (_gameLauncher?.IsGameRunning() == true)
            {
                return (true, string.Empty);
            }

            if (wrapperProcess != null)
            {
                wrapperProcess.Refresh();
                if (wrapperProcess.HasExited && wrapperProcess.ExitCode != 0)
                {
                    return (false, GetWrapperExitMessage(wrapperProcess.ExitCode));
                }
            }

            return (false, "The game did not stay open after launch.");
        }

        private void RestoreReadyState(string message, bool isError)
        {
            var state = HasLauncherSession()
                ? LauncherSessionState.LoggedIn
                : LauncherSessionState.LoggedOut;

            UpdateSessionState(state, message, isError);
            RefreshPrimaryActionButton();
            SetPatchProgressText(isError ? "Review the message above and try again." : "Ready to launch.");
        }

        private async Task LaunchLegacyGameAsync()
        {
            if (_gameLauncher == null)
            {
                return;
            }

            btnStartGame.IsEnabled = false;
            btnStartGame.Content = "STARTING...";

            try
            {
                SetPatchStatus("Starting game client...", false);
                SetPatchProgressText("Direct launch is active.");
                UpdateSessionState(LauncherSessionState.LoggedOut, "Starting the game directly...", false);

                var result = _gameLauncher.LaunchGame();
                if (!result.Success)
                {
                    AppendLog(result.Message);
                    RestoreReadyState("The game could not be started.", true);
                    return;
                }

                var launchCheck = await WaitForGameLaunchAsync(result.Process);
                if (!launchCheck.Success)
                {
                    AppendLog("Legacy launch failed: " + launchCheck.FailureMessage);
                    RestoreReadyState(launchCheck.FailureMessage, true);
                    return;
                }

                UpdateSessionState(LauncherSessionState.GameRunning, "Game launched.", false);
                Close();
            }
            catch (Exception ex)
            {
                AppendLog("Legacy launch failed: " + ex.Message);
                RestoreReadyState("The game could not be started.", true);
            }
        }

        private string GetTrayProcessName()
        {
            return Path.GetFileNameWithoutExtension(_config.SecurityTrayExecutable);
        }

        private static string GetWrapperExitMessage(int exitCode)
        {
            return exitCode switch
            {
                10 => "The game wrapper was started incorrectly.",
                11 => "The game start payload could not be loaded.",
                12 => "The game binary is missing from the client folder.",
                13 => "The game process could not be created.",
                14 => "The game process closed immediately after launch.",
                99 => "The game wrapper encountered an unexpected internal error.",
                _ => $"The game wrapper stopped with exit code {exitCode}."
            };
        }

        private static string GetLauncherVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.3.1";
        }

        private bool HasLauncherSession()
        {
            return _launcherSession != null && !string.IsNullOrWhiteSpace(_launcherSession.AccessToken);
        }

        private async Task<bool> AttemptInlineLoginAsync()
        {
            if (_loginService == null)
            {
                return false;
            }

            var username = txtInlineUsername.Text.Trim();
            var password = GetInlinePassword();

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ShowLoginError("Username und Passwort sind erforderlich.");
                UpdateSessionState(LauncherSessionState.LoggedOut, string.Empty, true);
                return false;
            }

            btnStartGame.IsEnabled = false;
            btnStartGame.Content = "LOGIN...";
            HideLoginError();

            UpdateSessionState(LauncherSessionState.Authenticating, "Authenticating...", false);
            SetPatchProgressText("Waiting for the login response...");
            AppendLog("Submitting launcher login request.");

            var loginResponse = await _loginService.LoginAsync(
                username,
                password,
                _machineId,
                GetLauncherVersion());

            if (!loginResponse.Success)
            {
                AppendLog("Launcher login failed: " + loginResponse.Message);
                ShowLoginError(loginResponse.Message);
                UpdateSessionState(LauncherSessionState.LoggedOut, string.Empty, true);
                RefreshPrimaryActionButton();
                return false;
            }

            _launcherSession = loginResponse;
            _loggedInUsername = username;
            AppendLog("Launcher login succeeded.");
            ClearInlinePassword();
            HideLoginError();
            ApplyAuthenticationUiState();
            UpdateSessionState(
                LauncherSessionState.LoggedIn,
                string.Empty,
                false);
            SetPatchProgressText("Login complete. Ready to start.");
            RefreshPrimaryActionButton();
            return true;
        }

        private void ApplyAuthenticationUiState()
        {
            var isLoggedIn = HasLauncherSession();
            loginFormPanel.Visibility = isLoggedIn ? Visibility.Collapsed : Visibility.Visible;
            sessionReadyPanel.Visibility = isLoggedIn ? Visibility.Visible : Visibility.Collapsed;

            txtLoginPanelTitle.Text = isLoggedIn ? "START GAME" : "LOGIN";
            txtLoginPanelSubtitle.Text = isLoggedIn
                ? string.Empty
                : string.Empty;
            txtSessionUser.Text = string.IsNullOrWhiteSpace(_loggedInUsername)
                ? "Authenticated"
                : _loggedInUsername;

            RefreshPrimaryActionButton();
        }

        private void ShowLoginError(string message)
        {
            txtLoginError.Text = message;
            txtLoginError.Visibility = Visibility.Visible;
        }

        private void HideLoginError()
        {
            txtLoginError.Text = string.Empty;
            txtLoginError.Visibility = Visibility.Collapsed;
        }

        private string GetInlinePassword()
        {
            return _isPasswordVisible ? txtInlinePassword.Text : pwdInlinePassword.Password;
        }

        private void ClearInlinePassword()
        {
            _isSyncingPassword = true;
            try
            {
                pwdInlinePassword.Password = string.Empty;
                txtInlinePassword.Text = string.Empty;
            }
            finally
            {
                _isSyncingPassword = false;
            }

            UpdatePasswordWatermark();
        }

        private void SetPasswordVisibility(bool isVisible)
        {
            _isPasswordVisible = isVisible;
            if (isVisible)
            {
                txtInlinePassword.Text = pwdInlinePassword.Password;
            }

            pwdInlinePassword.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
            txtInlinePassword.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            btnPasswordToggle.Content = isVisible ? "HIDE" : "VIEW";
            UpdatePasswordWatermark();
        }

        private void UpdatePasswordWatermark()
        {
            if (txtPasswordWatermark == null)
            {
                return;
            }

            var hasPassword = _isPasswordVisible
                ? !string.IsNullOrWhiteSpace(txtInlinePassword.Text)
                : !string.IsNullOrWhiteSpace(pwdInlinePassword.Password);

            txtPasswordWatermark.Visibility = hasPassword ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OpenUrl(string? url)
        {
            var targetUrl = string.IsNullOrWhiteSpace(url)
                ? "https://nanonline.net/"
                : url.Trim();

            Process.Start(new ProcessStartInfo
            {
                FileName = targetUrl,
                UseShellExecute = true
            });
        }

        private void AnimateLogo(double targetScale, double targetBlur, double targetOpacity)
        {
            var duration = TimeSpan.FromMilliseconds(180);

            var scaleX = new DoubleAnimation(targetScale, duration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            var scaleY = scaleX.Clone();
            var blurAnimation = new DoubleAnimation(targetBlur, duration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            var opacityAnimation = new DoubleAnimation(targetOpacity, duration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            logoScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
            logoScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
            logoShadowEffect.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, blurAnimation);
            logoShadowEffect.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, opacityAnimation);
        }

        private void LogoButton_Click(object sender, RoutedEventArgs e)
        {
            BtnVisitWebsite_Click(sender, e);
        }

        private void LogoButton_MouseEnter(object sender, MouseEventArgs e)
        {
            AnimateLogo(1.04, 42, 1);
        }

        private void LogoButton_MouseLeave(object sender, MouseEventArgs e)
        {
            AnimateLogo(1, 34, 0.92);
        }

        private void BtnPasswordToggle_Click(object sender, RoutedEventArgs e)
        {
            SetPasswordVisibility(!_isPasswordVisible);
        }

        private void PwdInlinePassword_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isSyncingPassword)
            {
                return;
            }

            if (!_isPasswordVisible)
            {
                UpdatePasswordWatermark();
                return;
            }

            _isSyncingPassword = true;
            try
            {
                txtInlinePassword.Text = pwdInlinePassword.Password;
            }
            finally
            {
                _isSyncingPassword = false;
            }

            UpdatePasswordWatermark();
        }

        private void TxtInlinePassword_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isSyncingPassword)
            {
                return;
            }

            if (!_isPasswordVisible)
            {
                UpdatePasswordWatermark();
                return;
            }

            _isSyncingPassword = true;
            try
            {
                pwdInlinePassword.Password = txtInlinePassword.Text;
            }
            finally
            {
                _isSyncingPassword = false;
            }

            UpdatePasswordWatermark();
        }

        private async void PasswordInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || _isPatching)
            {
                return;
            }

            e.Handled = true;

            if (HasLauncherSession())
            {
                await LaunchGameAsync();
                return;
            }

            await AttemptInlineLoginAsync();
        }

        private void BtnForgotPassword_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OpenUrl(_config.ForgotPasswordUrl);
            }
            catch (Exception ex)
            {
                SetActionStatus("The recovery page could not be opened.", true);
                AppendLog(string.Format("Forgot password open failed: {0}", ex.Message));
            }
        }
    }
}
