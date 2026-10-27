using EFT;
using EFT.UI;
using EFT.UI.BattleTimer;
using Redeploy.Core.GameAccess;
using HarmonyLib;
using System;
using System.Reflection;
using System.Text;
using UnityEngine;



namespace Redeploy.Core.View;



public static class RaidUiHelper

{

    private static readonly FieldInfo OverlapBlackImageField =

        AccessTools.Field(typeof(PreloaderUI), "_overlapBlackImage");



    private static readonly FieldInfo BlackScreenCoroutineField =

        AccessTools.Field(typeof(PreloaderUI), "_showBlackImageCoroutine");



    private static readonly FieldInfo MainTimerPanelField =

        AccessTools.Field(typeof(ExtractionTimersPanel), "_mainTimerPanel");



    private static readonly FieldInfo TimerStringBuilderField =

        AccessTools.Field(typeof(ExtractionTimersPanel), "_stringBuilder");



    public static void ClearBlackScreen()

    {

        if (!MonoBehaviourSingleton<PreloaderUI>.Instantiated)

            return;



        try

        {

            var preloader = MonoBehaviourSingleton<PreloaderUI>.Instance;



            var coroutine = BlackScreenCoroutineField.GetValue(preloader) as Coroutine;

            if (coroutine != null)

            {

                preloader.StopCoroutine(coroutine);

                BlackScreenCoroutineField.SetValue(preloader, null);

            }



            preloader.SetBlackImageAlpha(0f);



            if (OverlapBlackImageField.GetValue(preloader) is Component blackImage)

                blackImage.gameObject.SetActive(false);

        }

        catch (Exception ex)

        {

            CoreLog.LogWarning($"ClearBlackScreen failed: {ex.Message}");

        }

    }



    public static void RestoreInputAfterDeath()

    {

        GamePlayerOwner.SetIgnoreInput(false);

        GamePlayerOwner.SetIgnoreInputInNPCDialog(false);

        GamePlayerOwner.SetIgnoreInputWithKeepResetLook(false);

    }



    /// <summary>

    /// Re-shows the raid timer UI without calling SetTime, which would duplicate exfil entries.

    /// </summary>

    public static void RestoreExtractionTimer(LocalGame game)

    {

        if (game == null)

            return;



        try

        {

            var timer = LocalGameAccessor.GetGameUI(game)?.TimerPanel;

            if (timer == null)

                return;



            timer.ShowGameObject();

            timer.Reveal();

            timer.ProfileId = game.Profile.Id;

            timer.ForceUpdateExfiltrationPointsVisitedStatus();



            if (MainTimerPanelField.GetValue(timer) is MainTimerPanel mainTimer

                && TimerStringBuilderField.GetValue(timer) is StringBuilder builder)

            {

                mainTimer.Show(

                    DateTimeExtensions.UtcNow.AddSeconds(game.GameTimer.EscapeTimeSeconds()),

                    builder,

                    EMainTimerState.FindEp);

            }



            if (MonoBehaviourSingleton<PreloaderUI>.Instantiated)

                MonoBehaviourSingleton<PreloaderUI>.Instance.RaidInfoVisibility = true;

        }

        catch (Exception ex)

        {

            CoreLog.LogWarning($"RestoreExtractionTimer failed: {ex.Message}");

        }

    }

}


