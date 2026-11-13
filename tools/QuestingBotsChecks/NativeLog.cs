namespace Redeploy.Core { internal static class CoreLog { internal static void LogWarning(object value) { } } }
namespace Redeploy.Core.Engine { internal static class RespawnEngineState { internal static bool IsPipelineRunning { get; set; } } }
namespace Redeploy.Core.Api { internal static class RespawnApi { internal static event System.Action<EFT.Player, EFT.Player, string> Replaced { add { } remove { } } } }
