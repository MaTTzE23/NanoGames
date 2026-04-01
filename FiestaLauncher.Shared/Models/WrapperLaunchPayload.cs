using System.Text.Json.Serialization;

namespace FiestaLauncher.Shared.Models;

public sealed class WrapperLaunchPayload
{
    [JsonPropertyName("launchId")]
    public string LaunchId { get; set; } = string.Empty;

    [JsonPropertyName("loginHost")]
    public string LoginHost { get; set; } = string.Empty;

    [JsonPropertyName("loginPort")]
    public int LoginPort { get; set; }

    [JsonPropertyName("oskServer")]
    public string OskServer { get; set; } = string.Empty;

    [JsonPropertyName("oskStore")]
    public string OskStore { get; set; } = string.Empty;

    [JsonPropertyName("startToken")]
    public string StartToken { get; set; } = string.Empty;

    [JsonPropertyName("clientRoot")]
    public string ClientRoot { get; set; } = string.Empty;

    [JsonPropertyName("gameBinary")]
    public string GameBinary { get; set; } = string.Empty;
}
