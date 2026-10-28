using System;
using System.Collections.Generic;
using System.Reflection;
using EFT.UI;
using EFT.UI.BattleTimer;
using HarmonyLib;
using UnityEngine;

namespace Redeploy.Services;

/// <summary>Completes native Close before rebuilding the same panel for another faction.</summary>
internal static class ScavExtractionUi
{
    private static readonly FieldInfo CustomTimers = AccessTools.Field(typeof(ExtractionTimersPanel), "_customTimers");
    private static readonly FieldInfo EventTimer = AccessTools.Field(typeof(ExtractionTimersPanel), "_customEventTimer");
    private static readonly FieldInfo BtrTimer = AccessTools.Field(typeof(ExtractionTimersPanel), "_btrTimer");

    public static void ValidateContract()
    {
        if (CustomTimers?.FieldType != typeof(Dictionary<string, CustomTimerPanel>)
            || EventTimer?.FieldType != typeof(CustomTimerPanel) || BtrTimer?.FieldType != typeof(CustomTimerPanel))
            throw new MissingFieldException("The native extraction custom-timer layout could not be verified.");
    }

    public static void ValidateOwnedTimers(ExtractionTimersPanel panel)
    {
        var timers = ReadTimers(panel);
        var eventTimer = (CustomTimerPanel)EventTimer.GetValue(panel);
        var btrTimer = (CustomTimerPanel)BtrTimer.GetValue(panel);
        RegularScavPolicy.ValidateOwnedTimer(timers, "EventTimer", eventTimer);
        RegularScavPolicy.ValidateOwnedTimer(timers, "BtrTimer", btrTimer);
        if (ReferenceEquals(eventTimer, panel._eventTimerTemplate) && eventTimer != null
            || ReferenceEquals(btrTimer, panel._btrTimerTemplate) && btrTimer != null)
            throw new InvalidOperationException("An extraction timer template cannot be retired as an instance.");
    }

    // Call only AFTER native Close: it releases BTR/global-event subscriptions.
    // Native Close omits these two dictionary entries and instances. Keeping BtrTimer
    // would also prevent SetTime from subscribing the replacement BTR timer again.
    public static void RetireNativeTimersAfterClose(ExtractionTimersPanel panel)
    {
        var timers = ReadTimers(panel);
        Retire(panel, timers, "EventTimer", EventTimer);
        Retire(panel, timers, "BtrTimer", BtrTimer);
    }

    private static Dictionary<string, CustomTimerPanel> ReadTimers(ExtractionTimersPanel panel)
        => CustomTimers.GetValue(panel) as Dictionary<string, CustomTimerPanel>
            ?? throw new InvalidOperationException("The extraction custom-timer registry is unavailable.");

    private static void Retire(ExtractionTimersPanel panel, Dictionary<string, CustomTimerPanel> timers,
        string key, FieldInfo field)
    {
        var timer = (CustomTimerPanel)field.GetValue(panel);
        RegularScavPolicy.RemoveOwnedTimer(timers, key, timer);
        field.SetValue(panel, null);
        if (timer == null) return;
        try { timer.Close(); }
        finally { UnityEngine.Object.Destroy(timer.gameObject); }
    }
}
