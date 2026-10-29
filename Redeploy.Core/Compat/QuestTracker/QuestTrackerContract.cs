using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.Quests;
using UnityEngine;
using Version = System.Version;

namespace Redeploy.Core.Compat;

internal sealed class QuestTrackerContract
{
    internal const string PluginGuid = "xyz.drakia.questtracker";
    internal static readonly Version ReviewedVersion = new(1, 7, 0, 0);
    internal Type Component;
    internal FieldInfo Player, World, Game, Controller, MapQuests, Location, Panel, Tracked;
    internal MethodInfo Update, Cache, Changed, SameMap;
    internal static QuestTrackerContract Resolve(Type plugin, string guid, Version reported)
    {
        var assembly = StandaloneHudContract.Plugin(plugin, guid, PluginGuid, reported, ReviewedVersion,
            "DrakiaXYZ.QuestTracker.QuestTrackerPlugin", "DrakiaXYZ-QuestTracker", ReviewedVersion);
        var component = assembly.GetType("DrakiaXYZ.QuestTracker.Components.QuestTrackerComponent", true);
        var panel = assembly.GetType("DrakiaXYZ.QuestTracker.Components.QuestTrackerPanelComponent", true);
        var data = assembly.GetType("DrakiaXYZ.QuestTracker.Helpers.TrackedQuestData", true);
        if (!typeof(MonoBehaviour).IsAssignableFrom(component) || !typeof(MonoBehaviour).IsAssignableFrom(panel))
            throw new MissingMemberException("Quest Tracker component inheritance changed");
        return new QuestTrackerContract
        {
            Component = component,
            Player = StandaloneHudContract.Field(component, "player", typeof(Player)),
            World = StandaloneHudContract.Field(component, "gameWorld", typeof(GameWorld)),
            Game = StandaloneHudContract.Field(component, "abstractGame", typeof(AbstractGame)),
            Controller = StandaloneHudContract.Field(component, "questController", typeof(QuestController)),
            MapQuests = StandaloneHudContract.Field(component, "mapQuests", typeof(List<Quest>)),
            Location = StandaloneHudContract.Field(component, "locationId", typeof(string)),
            Panel = StandaloneHudContract.Field(component, "panel", panel),
            Tracked = StandaloneHudContract.Field(component, "trackedQuests", typeof(Dictionary<,>).MakeGenericType(typeof(string), typeof(Tuple<,>).MakeGenericType(typeof(Quest), data))),
            Update = StandaloneHudContract.Method(component, "Update", typeof(void), false),
            Cache = StandaloneHudContract.Method(component, "CacheQuests", typeof(void), false),
            Changed = StandaloneHudContract.Method(component, "HaveQuestsChanged", typeof(bool), false),
            SameMap = StandaloneHudContract.Method(component, "IsSameMap", typeof(bool), false, typeof(string), typeof(string))
        };
    }
}
