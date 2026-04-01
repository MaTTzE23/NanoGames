using System.Text.Json.Serialization;

namespace FiestaLauncher.Shared.Models;

public sealed class LauncherLoginRequest
{
    [JsonPropertyName("Username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("Password")]
    public string Password { get; set; } = string.Empty;

    [JsonPropertyName("MachineId")]
    public string MachineId { get; set; } = string.Empty;

    [JsonPropertyName("LauncherVersion")]
    public string LauncherVersion { get; set; } = string.Empty;
}

public sealed class LauncherLoginResponse
{
    [JsonPropertyName("Success")]
    public bool Success { get; set; }

    [JsonPropertyName("Message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("AccessToken")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("AccountId")]
    public string AccountId { get; set; } = string.Empty;

    [JsonPropertyName("AccountStatus")]
    public int AccountStatus { get; set; }

    [JsonPropertyName("LoginHost")]
    public string LoginHost { get; set; } = string.Empty;

    [JsonPropertyName("LoginPort")]
    public int LoginPort { get; set; }

    [JsonPropertyName("OskServer")]
    public string OskServer { get; set; } = string.Empty;

    [JsonPropertyName("OskStore")]
    public string OskStore { get; set; } = string.Empty;

    [JsonPropertyName("ExpiresAt")]
    public string ExpiresAt { get; set; } = string.Empty;
}

public sealed class LauncherStartRequest
{
    [JsonPropertyName("AccessToken")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("MachineId")]
    public string MachineId { get; set; } = string.Empty;

    [JsonPropertyName("LauncherVersion")]
    public string LauncherVersion { get; set; } = string.Empty;
}

public sealed class LauncherStartResponse
{
    [JsonPropertyName("Success")]
    public bool Success { get; set; }

    [JsonPropertyName("Message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("StartToken")]
    public string StartToken { get; set; } = string.Empty;

    [JsonPropertyName("LoginHost")]
    public string LoginHost { get; set; } = string.Empty;

    [JsonPropertyName("LoginPort")]
    public int LoginPort { get; set; }

    [JsonPropertyName("OskServer")]
    public string OskServer { get; set; } = string.Empty;

    [JsonPropertyName("OskStore")]
    public string OskStore { get; set; } = string.Empty;

    [JsonPropertyName("StartExpiresAt")]
    public string StartExpiresAt { get; set; } = string.Empty;
}

public sealed class LauncherHeartbeatRequest
{
    [JsonPropertyName("SessionToken")]
    public string SessionToken { get; set; } = string.Empty;
}

public sealed class LauncherHeartbeatResponse
{
    [JsonPropertyName("Success")]
    public bool Success { get; set; }

    [JsonPropertyName("Message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("IsActive")]
    public bool IsActive { get; set; }

    [JsonPropertyName("Status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("SecurityState")]
    public string SecurityState { get; set; } = string.Empty;

    [JsonPropertyName("HeartbeatIntervalSeconds")]
    public int HeartbeatIntervalSeconds { get; set; }
}

public sealed class SecurityEventRequest
{
    [JsonPropertyName("SessionToken")]
    public string SessionToken { get; set; } = string.Empty;

    [JsonPropertyName("EventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("FilePath")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("ExpectedSha256")]
    public string ExpectedSha256 { get; set; } = string.Empty;

    [JsonPropertyName("ActualSha256")]
    public string ActualSha256 { get; set; } = string.Empty;

    [JsonPropertyName("ProcessRunning")]
    public bool ProcessRunning { get; set; }

    [JsonPropertyName("OccurredAt")]
    public string OccurredAt { get; set; } = string.Empty;
}
