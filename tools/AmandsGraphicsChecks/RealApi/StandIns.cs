using System;
using EFT;
namespace Redeploy.Core { public static class CoreLog { public static void LogWarning(string text) { } public static void LogDebug(string text) { } } }
namespace Redeploy.Core.Api { public static class RespawnApi { public static event Action<Player, Player, string> Replaced { add { } remove { } } } }
