using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace CorpseRun.Services;

internal static class LocalGameAccessor
{
    private static readonly MethodInfo DeathHandlerMethod =
        AccessTools.Method(typeof(BaseLocalGame<EftGamePlayerOwner>), "method_19");

    private static readonly MethodInfo DeathScreenMethod =
        AccessTools.Method(typeof(BaseLocalGame<EftGamePlayerOwner>), "method_11");

    private static readonly MethodInfo StatisticsShotMethod =
        AccessTools.Method(typeof(BaseLocalGame<EftGamePlayerOwner>), "method_5");

    private static Traverse GameTraverse(LocalGame game) => Traverse.Create(game);

    public static EftGamePlayerOwner GetPlayerOwner(LocalGame game)
        => GameTraverse(game).Field("gparam_0").GetValue<EftGamePlayerOwner>();

    public static void SetPlayerOwner(LocalGame game, EftGamePlayerOwner owner)
        => GameTraverse(game).Field("gparam_0").SetValue(owner);

    public static EftGamePlayerOwner CreatePlayerOwner(LocalGame game, LocalPlayer player)
    {
        var factory = GameTraverse(game).Field("func_1").GetValue<Delegate>();
        return (EftGamePlayerOwner)factory.DynamicInvoke(player);
    }

    public static BotsController GetBotsController(LocalGame game)
    {
        if (game is IBotGame botGame)
            return botGame.BotsController;

        return GameTraverse(game).Field("botsController_0").GetValue<BotsController>();
    }

    public static int GetNextPlayerId(LocalGame game)
    {
        var traverse = GameTraverse(game).Field("int_0");
        var current = traverse.GetValue<int>();
        current++;
        traverse.SetValue(current);
        return current;
    }

    public static void ReplacePlayerInDictionary(LocalGame game, LocalPlayer player)
    {
        var dict = GameTraverse(game).Field("dictionary_0").GetValue<Dictionary<string, Player>>();
        dict[player.ProfileId] = player;
    }

    public static ISession GetSession(LocalGame game)
        => GameTraverse(game).Field("iSession").GetValue<ISession>();

    public static LocalRaidSettings GetLocalRaidSettings(LocalGame game)
        => GameTraverse(game).Field("localRaidSettings_0").GetValue<LocalRaidSettings>();

    public static GameUI GetGameUI(LocalGame game)
        => GameTraverse(game).Field("gameUI_0").GetValue<GameUI>();

    public static void WirePlayerGameEvents(LocalGame game, LocalPlayer player)
    {
        if (game == null || player == null)
            return;

        player.OnEpInteraction += game.OnEpInteraction;

        var shotHandler = (Action<Item, AmmoItemClass>)Delegate.CreateDelegate(
            typeof(Action<Item, AmmoItemClass>),
            game,
            StatisticsShotMethod);
        player.OnStatisticsShot = (Action<Item, AmmoItemClass>)Delegate.Combine(player.OnStatisticsShot, shotHandler);
    }

    public static void SubscribeDeathHandler(LocalGame game, LocalPlayer player)
    {
        var handler = DeathHandlerMethod.CreateDelegate(typeof(Action<EDamageType>), game);
        player.HealthController.DiedEvent += (Action<EDamageType>)handler;
    }

    public static void UnsubscribeDeathHandler(LocalGame game, LocalPlayer player)
    {
        if (player?.HealthController == null)
            return;

        var handler = DeathHandlerMethod.CreateDelegate(typeof(Action<EDamageType>), game);
        player.HealthController.DiedEvent -= (Action<EDamageType>)handler;
    }

    public static void InvokeDeathScreen(LocalGame game)
        => DeathScreenMethod.Invoke(game, null);
}
