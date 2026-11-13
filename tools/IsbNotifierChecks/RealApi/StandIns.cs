using System;
using EFT;
namespace Redeploy.Core { internal static class CoreLog { internal static void LogWarning(string text) { } internal static void LogDebug(string text) { } } }
namespace Redeploy.Core.Api
{
    internal static class RespawnApi
    {
        internal static event Action<Player, Player, string> Replaced { add { } remove { } }
        internal static event Action<Player, string> ReplacementFailed { add { } remove { } }
    }
}
