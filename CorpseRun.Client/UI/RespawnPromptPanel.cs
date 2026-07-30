using CorpseRun.Config;
using CorpseRun.Services;
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
    private Texture2D _dimTex;
    private bool _stylesReady;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Show()
    {
        _visible = true;
        _windowRect = default;
        Plugin.ModLogger.LogInfo("[CorpseRun] Respawn prompt shown.");
    }

    public void Hide()
    {
        _visible = false;
    }

    private void Update()
    {
        if (!_visible || !RespawnSessionState.PendingRespawnChoice)
            return;

        // The game's input manager keeps re-locking the cursor, so enforce it every frame while the prompt is open.
        if (Cursor.lockState != CursorLockMode.None || !Cursor.visible)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void OnGUI()
    {
        if (!_visible || !RespawnSessionState.PendingRespawnChoice)
            return;

        EnsureStyles();

        GUI.depth = 100000;

        var fullScreen = new Rect(0f, 0f, Screen.width, Screen.height);
        GUI.color = new Color(1f, 1f, 1f, 0.72f);
        GUI.DrawTexture(fullScreen, _dimTex);
        GUI.color = Color.white;

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

        _dimTex = new Texture2D(1, 1);
        _dimTex.SetPixel(0, 0, Color.black);
        _dimTex.Apply();

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
