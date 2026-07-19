using System.Text.Json;

namespace SteamShare.Core.Models;

/// <summary>
/// JSON payload inside a share key. Format:
/// { "encrypted": bool, "id": &lt;workshop publish id&gt; }
/// NOTE: "encrypted" is correct spelling (NOT "encypted").
/// </summary>
public sealed record ShareKeyPayload
{
    public bool Encrypted { get; init; }

    public ulong Id { get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static ShareKeyPayload FromJson(string json) =>
        JsonSerializer.Deserialize<ShareKeyPayload>(json, JsonOptions)
        ?? throw new FormatException("Invalid share key payload JSON");
}
