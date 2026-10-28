using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Utils;

namespace Redeploy.Server.Scav;

public sealed record ScavPrepareRequest : IRequestData
{
    [JsonPropertyName("protocolVersion")] public int ProtocolVersion { get; set; }
    [JsonPropertyName("initialServerId")] public string InitialServerId { get; set; } = "";
    [JsonPropertyName("clientRequestId")] public string ClientRequestId { get; set; } = "";
    [JsonPropertyName("frozenPmcDeath")] public EndLocalRaidRequestData? FrozenPmcDeath { get; set; }
}

public sealed record ScavControlRequest : IRequestData
{
    [JsonPropertyName("token")] public string Token { get; set; } = "";
    [JsonPropertyName("initialServerId")] public string InitialServerId { get; set; } = "";
    [JsonPropertyName("scavProfileId")] public string ScavProfileId { get; set; } = "";
    [JsonPropertyName("generationHash")] public string GenerationHash { get; set; } = "";
}

public sealed record ScavResponse
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("token")] public string? Token { get; set; }
    [JsonPropertyName("phase")] public string? Phase { get; set; }
    [JsonPropertyName("scavProfile")] public PmcData? ScavProfile { get; set; }
    [JsonPropertyName("expiresAt")] public long? ExpiresAt { get; set; }
    [JsonPropertyName("generationHash")] public string? GenerationHash { get; set; }
    [JsonPropertyName("availableAt")] public double? AvailableAt { get; set; }
    public static ScavResponse Rejected(string reason) => new() { Error = reason };
}
