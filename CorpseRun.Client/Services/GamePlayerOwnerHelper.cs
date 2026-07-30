using EFT;
using HarmonyLib;
using System.Reflection;

namespace CorpseRun.Services;

internal static class GamePlayerOwnerHelper
{
    private static readonly FieldInfo MyPlayerField =
        AccessTools.Field(typeof(GamePlayerOwner), "player_1");

    public static void SetMyPlayer(Player player)
    {
        MyPlayerField.SetValue(null, player);
    }

    public static Player GetMyPlayer()
        => MyPlayerField.GetValue(null) as Player;
}
