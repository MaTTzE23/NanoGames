using System.Text.Json.Serialization;

namespace FiestaLauncher.Shared.Models;

public sealed class SecurityTrayCommand
{
    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("startToken")]
    public string StartToken { get; set; } = string.Empty;

    [JsonPropertyName("heartbeatUrl")]
    public string HeartbeatUrl { get; set; } = string.Empty;

    [JsonPropertyName("securityEventUrl")]
    public string SecurityEventUrl { get; set; } = string.Empty;

    [JsonPropertyName("clientRoot")]
    public string ClientRoot { get; set; } = string.Empty;

    [JsonPropertyName("gameProcessName")]
    public string GameProcessName { get; set; } = string.Empty;

    [JsonPropertyName("expectedHashes")]
    public Dictionary<string, string> ExpectedHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("heartbeatIntervalSeconds")]
    public int HeartbeatIntervalSeconds { get; set; } = 30;
}

public sealed class SecurityTrayResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; } = true;
}
