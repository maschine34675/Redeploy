using System;
using EFT;
namespace Redeploy.Core { internal static class CoreLog { internal static void LogWarning(string message) { } } }
namespace Redeploy.Core.Api
{
    internal static class RespawnApi
    {
        internal static event Action<Player, Player, string> Replaced;
        internal static event Action<Player, string> ReplacementFailed;
    }
}
