namespace Redeploy.Core { internal static class CoreLog { internal static void LogDebug(string text) { } internal static void LogWarning(string text) { } } }
namespace Redeploy.Core.Api { internal static class RespawnApi { internal static event System.Action<EFT.Player, EFT.Player, string> Replaced; } }
