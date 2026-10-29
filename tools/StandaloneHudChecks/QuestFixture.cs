using EFT;
using EFT.Quests;
using UnityEngine;
namespace DrakiaXYZ.QuestTracker
{
#if FUTURE_MODS
    [BepInEx.BepInPlugin("xyz.drakia.questtracker", "Quest Tracker", "1.7.1")]
#else
    [BepInEx.BepInPlugin("xyz.drakia.questtracker", "Quest Tracker", "1.7.0")]
#endif
    public sealed class QuestTrackerPlugin : BepInEx.BaseUnityPlugin { }
}
namespace DrakiaXYZ.QuestTracker.Helpers
{
    public class TrackedQuestData { public bool Tracked = true; }
    public static class QuestsTracker
    {
        public static readonly Dictionary<string, TrackedQuestData> Preferences = new();
        public static Profile PreferenceOwner;
        public static event Action Changed;
        public static void Change() => Changed?.Invoke();
        public static int Handlers => Changed?.GetInvocationList().Length ?? 0;
    }
}
namespace DrakiaXYZ.QuestTracker.Components
{
    public class QuestTrackerPanelComponent : MonoBehaviour
    {
        public bool Visible; public int Refreshes;
        public List<Quest> Map; public Dictionary<string, Tuple<Quest, Helpers.TrackedQuestData>> Tracked;
        public void SetQuests(Dictionary<string, Tuple<Quest, Helpers.TrackedQuestData>> tracked, List<Quest> map)
        { Refreshes++; Tracked = tracked; Map = map; }
    }
    public class QuestTrackerComponent : MonoBehaviour
    {
        private QuestTrackerPanelComponent panel;
        private GameWorld gameWorld;
        private AbstractGame abstractGame;
        private Player player;
        private QuestController questController;
        private List<Quest> mapQuests = new();
        private Dictionary<string, Tuple<Quest, Helpers.TrackedQuestData>> trackedQuests = new();
        private string locationId = "map";
        public float hideTime = 17;
        public int Hash;
        public int Updates;
        public Player Bound => player;
        public QuestController Controller => questController;
        public QuestTrackerPanelComponent Panel => panel;
        public void Initialize(LocalGame game, Player body)
        {
            gameWorld = game.GameWorld; abstractGame = game; player = body; questController = body.QuestController;
            panel = new(); gameWorld.gameObject.Add(this);
            mapQuests = questController.Quests.ToList(); CacheQuests(); HaveQuestsChanged();
            Helpers.QuestsTracker.Changed += RebuildCache;
        }
        private void RebuildCache() => Hooked(nameof(CacheQuests));
        public void OnDestroy() => Helpers.QuestsTracker.Changed -= RebuildCache;
        public void Hooked(string name)
        {
            var method = GetType().GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            method.Invoke(this, null);
        }
        private void Update() { Updates++; HaveQuestsChanged(); }
        private void CacheQuests()
        {
            trackedQuests.Clear();
            foreach (var entry in Helpers.QuestsTracker.Preferences)
            {
                var quest = questController.Quests.GetConditional(entry.Key);
                if (entry.Value.Tracked && quest != null) trackedQuests.Add(entry.Key, Tuple.Create(quest, entry.Value));
            }
            panel.SetQuests(trackedQuests, mapQuests);
        }
        private bool HaveQuestsChanged() { var hash = mapQuests.Sum(q => q.Progress); bool changed = hash != Hash; Hash = hash; return changed; }
        private bool IsSameMap(string quest, string map) => quest == map;
    }
}
