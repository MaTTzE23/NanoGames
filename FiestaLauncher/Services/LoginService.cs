using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FiestaLauncher.Models;
using FiestaLauncher.Shared.Models;
using Newtonsoft.Json;

namespace FiestaLauncher.Services
{
    public class LoginService : IDisposable
    {
        private readonly ServerConfig _config;
        private readonly HttpClient _httpClient;

        public LoginService(ServerConfig config)
        {
            _config = config;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        public async Task<LauncherLoginResponse> LoginViaApiAsync(string username, string password, string machineId, string launcherVersion)
        {
            try
            {
                var loginRequest = new LauncherLoginRequest
                {
                    Username = username,
                    Password = password,
                    MachineId = machineId,
                    LauncherVersion = launcherVersion
                };

                var json = JsonConvert.SerializeObject(loginRequest);
                var loginUrl = BuildLoginUrl();
                using var requestContent = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(loginUrl, requestContent);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var loginResponse = JsonConvert.DeserializeObject<LauncherLoginResponse>(responseBody);
                    return loginResponse ?? new LauncherLoginResponse
                    {
                        Success = false,
                        Message = "Ungueltige Server-Antwort"
                    };
                }

                return new LauncherLoginResponse
                {
                    Success = false,
                    Message = string.Format("Login fehlgeschlagen: {0}", response.StatusCode)
                };
            }
            catch (HttpRequestException ex)
            {
                return new LauncherLoginResponse
                {
                    Success = false,
                    Message = string.Format("Server nicht erreichbar: {0}", ex.Message)
                };
            }
            catch (Exception ex)
            {
                return new LauncherLoginResponse
                {
                    Success = false,
                    Message = string.Format("Fehler: {0}", ex.Message)
                };
            }
        }

        public async Task<LauncherStartResponse> RequestStartAsync(string accessToken, string machineId, string launcherVersion)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    return new LauncherStartResponse
                    {
                        Success = false,
                        Message = "AccessToken fehlt."
                    };
                }

                var request = new LauncherStartRequest
                {
                    AccessToken = accessToken,
                    MachineId = machineId,
                    LauncherVersion = launcherVersion
                };

                var json = JsonConvert.SerializeObject(request);
                using var requestContent = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(BuildStartUrl(), requestContent);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var startResponse = JsonConvert.DeserializeObject<LauncherStartResponse>(responseBody);
                    return startResponse ?? new LauncherStartResponse
                    {
                        Success = false,
                        Message = "Ungueltige Server-Antwort"
                    };
                }

                return new LauncherStartResponse
                {
                    Success = false,
                    Message = string.Format("Start fehlgeschlagen: {0}", response.StatusCode)
                };
            }
            catch (Exception ex)
            {
                return new LauncherStartResponse
                {
                    Success = false,
                    Message = string.Format("Startfehler: {0}", ex.Message)
                };
            }
        }

        public async Task<LauncherLoginResponse> LoginAsync(string username, string password, string machineId, string launcherVersion)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return new LauncherLoginResponse
                {
                    Success = false,
                    Message = "Benutzername und Passwort erforderlich!"
                };
            }

            return await LoginViaApiAsync(username, password, machineId, launcherVersion);
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }

        private string BuildLoginUrl()
        {
            var configuredUrl = (_config.LauncherLoginUrl ?? _config.LoginApiUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(configuredUrl))
                return "api/launcher/login.php";

            if (configuredUrl.EndsWith(".php", StringComparison.OrdinalIgnoreCase) ||
                configuredUrl.EndsWith("/launcher/login", StringComparison.OrdinalIgnoreCase) ||
                configuredUrl.EndsWith("/launcher/login.php", StringComparison.OrdinalIgnoreCase))
            {
                return configuredUrl;
            }

            if (!configuredUrl.EndsWith("/"))
                configuredUrl += "/";

            return configuredUrl + "api/launcher/login.php";
        }

        private string BuildStartUrl()
        {
            var configuredUrl = (_config.LauncherStartUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(configuredUrl))
                return "api/launcher/start.php";

            if (configuredUrl.EndsWith(".php", StringComparison.OrdinalIgnoreCase) ||
                configuredUrl.EndsWith("/launcher/start", StringComparison.OrdinalIgnoreCase) ||
                configuredUrl.EndsWith("/launcher/start.php", StringComparison.OrdinalIgnoreCase))
            {
                return configuredUrl;
            }

            if (!configuredUrl.EndsWith("/"))
                configuredUrl += "/";

            return configuredUrl + "api/launcher/start.php";
        }
    }
}
