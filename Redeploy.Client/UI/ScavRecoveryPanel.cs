using Redeploy.Services;
using UnityEngine;

namespace Redeploy.UI;

/// <summary>Recovery is opt-in, outside raids and only over the actual main menu.</summary>
public sealed class ScavRecoveryPanel : MonoBehaviour
{
    private GUIStyle _title;
    private GUIStyle _body;
    private GUIStyle _warning;
    private GUIStyle _button;
    private GUIStyle _window;
    private bool _confirmAbandon;
    private string _confirmationId;
    private Vector2 _scroll;

    private void Update() => ScavRecoveryClient.Tick();

    private void OnGUI()
    {
        var flow = ScavRecoveryClient.Flow;
        if (!ScavRecoveryClient.IsMainMenu || !flow.Visible)
            return;
        if (_confirmationId != flow.RecoveryId || flow.State != "abandon" || flow.Error != null)
        {
            _confirmAbandon = false;
            _confirmationId = flow.RecoveryId;
        }
        EnsureStyles();
        GUI.depth = -10000;
        float width = Mathf.Min(650f, Screen.width * 0.92f);
        float height = Mathf.Min(560f, Screen.height * 0.92f);
        var rect = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
        GUI.ModalWindow(842652, rect, Draw, "Redeploy — Scav recovery", _window);
    }

    private void Draw(int id)
    {
        var flow = ScavRecoveryClient.Flow;
        _scroll = GUILayout.BeginScrollView(_scroll);
        GUILayout.Space(12);
        GUILayout.Label(flow.State == "restart" ? "Recovery complete — restart required" : "Interrupted Scav continuation", _title);
        GUILayout.Space(14);
        string explanation = flow.State switch
        {
            "abandon" when flow.Action == "save" => "Your PMC remains dead. The server recorded a result but its save was interrupted.\n\nComplete that recorded save to continue recovery. Its recorded Scav result and cooldown are preserved, along with loot already transferred to your stash.",
            "abandon" => "Your PMC died before you continued as your Scav. That PMC death still applies.\n\nAbandoning ends the unfinished Scav life as a death. Its remaining loot is lost and the normal Scav cooldown applies. You cannot resume this raid.",
            "transfer" => "Your PMC remains dead. Your Scav extracted and still has loot to transfer.\n\nResume the native loot transfer to move the remaining items into your stash. Items already transferred stay in your stash.",
            "blocked" => "The server cannot yet confirm a safe recovery. New raids remain blocked to protect your inventory. Check status again after resolving the server error.",
            "restart" => "The recovery result is confirmed. Close and restart the game to load the updated PMC, Scav and cooldown before starting another raid.",
            _ => "The recovery check could not finish. You can retry the check or return to the menu."
        };
        GUILayout.Label(explanation, _body);
        if (!string.IsNullOrWhiteSpace(flow.Message) && flow.State != "none" && flow.State != "restart")
        {
            GUILayout.Space(8);
            GUILayout.Label(flow.Message.Length > 500 ? flow.Message.Substring(0, 500) : flow.Message, _body);
        }
        if (flow.Error != null)
        {
            GUILayout.Space(10);
            GUILayout.Label(flow.Error, _warning);
        }
        GUILayout.FlexibleSpace();
        bool previousEnabled = GUI.enabled;
        GUI.enabled = previousEnabled && !flow.Busy;
        if (flow.State == "abandon" && flow.Error == null)
        {
            if (!_confirmAbandon)
            {
                if (Button(flow.Action == "save" ? "Complete interrupted save…" : "Abandon interrupted raid…"))
                    _confirmAbandon = true;
            }
            else
            {
                GUILayout.Label(flow.Action == "save" ? "Confirm: complete the exact recorded result without replaying the raid." : "Confirm: lose the unfinished Scav loot and apply the normal death/cooldown.", _warning);
                if (Button(flow.Action == "save" ? "Confirm — complete interrupted save" : "Confirm — abandon interrupted raid"))
                {
                    _confirmAbandon = false;
                    ScavRecoveryClient.Abandon();
                }
                if (Button("Back"))
                    _confirmAbandon = false;
            }
        }
        else if (flow.State == "transfer" && flow.Error == null && Button("Resume loot transfer"))
            ScavRecoveryClient.ResumeTransfer();
        if ((flow.Error != null || flow.State == "blocked") && Button("Check status again"))
            ScavRecoveryClient.Retry();
        if (flow.State == "none" && !flow.NeedsRestart && Button("Return to menu"))
            flow.DismissUnavailable();
        GUI.enabled = previousEnabled;
        if (Button("Close game"))
            Application.Quit();
        if (flow.Busy)
            GUILayout.Label("Contacting the server…", _body);
        GUILayout.Space(8);
        GUILayout.EndScrollView();
    }

    private bool Button(string text) => GUILayout.Button(text, _button, GUILayout.Height(38));

    private void EnsureStyles()
    {
        if (_body != null)
            return;
        _title = new GUIStyle(GUI.skin.label) { fontSize = 21, fontStyle = FontStyle.Bold, wordWrap = true };
        _body = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true };
        _warning = new GUIStyle(_body);
        _warning.normal.textColor = new Color(1f, 0.76f, 0.35f);
        _button = new GUIStyle(GUI.skin.button) { fontSize = 16, wordWrap = true };
        _window = new GUIStyle(GUI.skin.window) { padding = new RectOffset(24, 24, 30, 20) };
    }
}
