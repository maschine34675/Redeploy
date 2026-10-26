using System.Runtime.CompilerServices;
using UnityEngine;
namespace EFT.AssetsManager
{
    public class AssetPoolObject : Component { }
    public class WeaponModPoolObject : AssetPoolObject { }
    public class WeaponPrefab : AssetPoolObject { }
    public class PoolContainerObject : Component { }
    public interface IAssetPool { }
    public class AssetPool<T> : IAssetPool where T : AssetPoolObject
    {
        public object IEasyAssets;
        public EFT.ResourceKey ResourceKey;
        public EFT.ObjectsFactory.AssemblyType AssemblyType;
        public Transform ParentTransform;
        public PoolContainerObject ParentObject = new GameObject().AddComponent<PoolContainerObject>();
        public Func<Transform, GameObject> Instantiate;
        [MethodImpl(MethodImplOptions.NoInlining)] public GameObject GetOriginalGameObject(Transform parent) => Instantiate(parent);
    }
}
namespace EFT
{
    public sealed record ResourceKey(string path, string rcid = null);
    public class ObjectsFactory
    {
        public enum PoolsCategory { Raid, Menu }
        public enum AssemblyType { Local, Online }
        public class Pools
        {
            public class AssetPoolData { public TaskCompletionSource<EFT.AssetsManager.IAssetPool> Source; }
            public Transform UnloadableParent = new GameObject().transform;
            public Transform _dontDestroyParent = new GameObject().transform;
            public Dictionary<ResourceKey, AssetPoolData> PoolsDictionary = new();
        }
        public object EasyAssets = new();
        public Pools RaidPools = new();
        public Pools GetPools(PoolsCategory category) => RaidPools;
    }
}
namespace ArenaMode
{
    public static class Plugin
    {
        public const string PluginGuid = "com.maschine.ArenaMode";
        public static readonly FakeLogger ModLogger = new();
    }
    public sealed class FakeLogger
    { public void LogDebug(string text) { } public void LogWarning(string text) { } }
}
namespace ArenaMode.Session { public static class ArenaSession { public static bool IsActive, ArmedForNextRaid; } }
