using EFT;
using UnityEngine;
using System.Reflection;
using System.Runtime.CompilerServices;

// Minimal foreign-shape fixtures, not a copy of Enemy Markers. The two reviewed
// defects are deliberately present so the production adapter must correct them.
namespace flir.enemymarkers
{
    [BepInEx.BepInPlugin("flir.enemymarkers", "Enemy Markers", "1.4.0")]
    public class Plugin : BepInEx.BaseUnityPlugin { }
    public class PlayerInfo { public GameObject Marker; public int PersistentState = 42; }
    public class EmComponent : MonoBehaviour
    {
        private GameWorld _gameWorld;
        private Dictionary<IPlayer, PlayerInfo> _markersDict = new();
        public bool OriginalCleanupCompleted;
        private class Billboard : MonoBehaviour
        {
            private Camera _mainCamera;
            public void BindOriginal(Camera camera) => _mainCamera = camera;
            [MethodImpl(MethodImplOptions.NoInlining)]
            private void LateUpdate() { if (_mainCamera) transform.rotation = _mainCamera.transform.rotation; }
        }
        public void Attach(GameWorld world) { _gameWorld = world; world.OnPersonAdd += OnPersonAdd; }
        public IReadOnlyDictionary<IPlayer, PlayerInfo> Markers => _markersDict;
        public object BillboardFor(IPlayer body) => _markersDict[body].Marker.GetComponent(typeof(Billboard));
        public void Add(IPlayer body) => OnPersonAdd(body);
        public void Gone(IPlayer body) => PlayerDeadOrUnspawn(body);
        public void Initialize() => InitializeMarkers();
        public void Teardown() => OnDestroy();
        public static void Orient(object billboard) => billboard.GetType().GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(billboard, null);
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void OnPersonAdd(IPlayer body)
        {
            body.OnIPlayerDeadOrUnspawn += PlayerDeadOrUnspawn;
            var go = new GameObject(); var b = (Billboard)go.AddComponent(typeof(Billboard)); b.BindOriginal(Camera.main);
            _markersDict.Add(body, new PlayerInfo { Marker = go });
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void PlayerDeadOrUnspawn(IPlayer body) { body.OnIPlayerDeadOrUnspawn -= PlayerDeadOrUnspawn; _ = _markersDict[body]; _markersDict.Remove(body); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void OnDestroy()
        {
            _gameWorld.OnPersonAdd -= OnPersonAdd;
            foreach (var key in _markersDict.Keys)
            {
                if (key is not Player body) continue;
                try { body.OnPlayerDeadOrUnspawn -= PlayerDeadOrUnspawn; _ = body.name; }
                catch { _ = body.name; }
            }
            _markersDict.Clear(); _gameWorld = null; OriginalCleanupCompleted = true;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void InitializeMarkers()
        {
            foreach (Player body in _gameWorld.RegisteredPlayers)
                if (!body.IsYourPlayer) OnPersonAdd(body);
        }
    }
}
public class NewGamePatch
{
    [MethodImpl(MethodImplOptions.NoInlining)] public static void PatchPrefix() { }
}
