using EFT;
using HarmonyLib;
using System.Reflection;

namespace Redeploy.Core.GameAccess;

public static class GamePlayerOwnerHelper
{
    private static readonly FieldInfo MyPlayerField =
        AccessTools.Field(typeof(GamePlayerOwner), "_myPlayer");

    public static void SetMyPlayer(Player player)
    {
        MyPlayerField.SetValue(null, player);
    }

    public static Player GetMyPlayer()
        => MyPlayerField.GetValue(null) as Player;
}
