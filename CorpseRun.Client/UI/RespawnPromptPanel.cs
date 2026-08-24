using CorpseRun.Config;
using CorpseRun.Services;
using EFT.GlobalEvents;
using UnityEngine;

namespace CorpseRun.UI;

public class RespawnPromptPanel : MonoBehaviour
{
    public static RespawnPromptPanel Instance { get; private set; }

    private bool _visible;
    private Rect _windowRect;
    private GUIStyle _titleStyle;
    private GUIStyle _bodyStyle;
    private GUIStyle _warningStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _windowStyle;
    private bool _stylesReady;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        // Unconditional: the flag below is the game's, and leaving it set for a
        // panel that no longer exists would strand a cursor on screen.
        SetGameCursor(false);
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Asks the game to show or hide the cursor, rather than setting it here.
    /// The game decides once per frame what the cursor state should be and
    /// writes it only when the live state disagrees - so setting
    /// <c>Cursor.visible</c> is exactly what makes it disagree, and the two
    /// alternate at frame rate. That was the flicker. Its own flag is OR-ed
    /// into that decision, so once it is set there is nothing to disagree
    /// about, and the single write the game then performs restores
    /// visibility, lock mode and the cursor bitmap together.
    /// </summary>
    private static void SetGameCursor(bool show)
    {
        GlobalEventsController.Instance
            .CreateCommonEvent<ToggleShowInGameCursorEvent>()
            .Invoke(show);
    }

    public void Show()
    {
        _visible = true;
        _windowRect = default;
        SetGameCursor(true);
        Plugin.ModLogger.LogInfo("Respawn prompt shown.");
    }

    public void Hide()
    {
        _visible = false;
        // Unconditional, so a second Hide costs nothing: the game's flag is a
        // plain bool with no counter. Every path that closes this prompt has
        // to come through here, because the cursor no longer returns by itself
        // once the mod stops asking for it.
        SetGameCursor(false);
    }

    private void Update()
    {
        if (!_visible || !RespawnSessionState.PendingRespawnChoice)
            return;

        // Not a write: reading creates no disagreement for the game to correct.
        // The flag is global and uncounted, so another mod - or the game's own
        // world, which uses it too - can clear it while this prompt is up. Ask
        // again only when the cursor is actually gone.
        if (!Cursor.visible)
            SetGameCursor(true);
    }

    private void OnGUI()
    {
        if (!_visible || !RespawnSessionState.PendingRespawnChoice)
            return;

        EnsureStyles();

        GUI.depth = 100000;

        // No full-screen dim: the background stays visible on purpose so a
        // KillcamReplay death replay can play behind the prompt.

        var width = Mathf.Min(520f, Screen.width * 0.9f);
        var height = Settings.RequiredRespawnItem.Value != "None" ? 320f : 290f;
        if (_windowRect.width < 1f)
            _windowRect = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);

        _windowRect = GUI.ModalWindow(842651, _windowRect, DrawWindow, "CorpseRun", _windowStyle);
    }

    private void DrawWindow(int id)
    {
        GUILayout.Space(8);
        GUILayout.Label("You died", _titleStyle);
        GUILayout.Space(6);

        var remaining = Settings.MaxRespawnsPerRaid.Value <= 0
            ? "unlimited"
            : (Settings.MaxRespawnsPerRaid.Value - RespawnSessionState.RespawnsUsed).ToString();

        GUILayout.Label(
            $"Respawn without gear and recover your equipment from your corpse.\nRespawns remaining: {remaining}",
            _bodyStyle);

        GUILayout.Space(8);

        var itemRequired = Settings.RequiredRespawnItem.Value != "None";
        var canRespawn = !itemRequired || RespawnSessionState.RespawnItemAvailable;

        if (itemRequired)
        {
            GUILayout.Label(
                canRespawn
                    ? $"Respawning consumes: {Settings.RequiredRespawnItem.Value}"
                    : $"Respawn unavailable: no {Settings.RequiredRespawnItem.Value} in inventory!",
                canRespawn ? _bodyStyle : _warningStyle);
            GUILayout.Space(6);
        }

        if (canRespawn)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Respawn near corpse", _buttonStyle, GUILayout.Width(190f), GUILayout.Height(36f)))
                RespawnService.BeginRespawn(RespawnLocationMode.NearCorpse);

            GUILayout.Space(12);

            if (GUILayout.Button("Respawn at spawn point", _buttonStyle, GUILayout.Width(190f), GUILayout.Height(36f)))
                RespawnService.BeginRespawn(RespawnLocationMode.InfilSpawn);

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
        }

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("End raid", _buttonStyle, GUILayout.Width(190f), GUILayout.Height(36f)))
            RespawnService.GiveUpRaid();

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(10);
        GUI.DragWindow();
    }

    private void EnsureStyles()
    {
        if (_stylesReady)
            return;

        _windowStyle = new GUIStyle(GUI.skin.window)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold
        };

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        _titleStyle.normal.textColor = new Color(0.95f, 0.75f, 0.55f);

        _bodyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            wordWrap = true,
            alignment = TextAnchor.UpperCenter
        };
        _bodyStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);

        _warningStyle = new GUIStyle(_bodyStyle)
        {
            fontStyle = FontStyle.Bold
        };
        _warningStyle.normal.textColor = new Color(0.95f, 0.4f, 0.35f);

        _buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold
        };

        _stylesReady = true;
    }
}
