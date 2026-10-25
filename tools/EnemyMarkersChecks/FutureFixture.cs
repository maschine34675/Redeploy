using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using EFT;
using UnityEngine;

internal static class FutureFixture
{
    internal static Type Create(bool brokenCamera, bool brokenDestroy = false)
    {
        var version = new Version(1, 4, 1, 0);
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("flir.enemymarkers") { Version = version }, AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("future");
        void Method(TypeBuilder owner, string name, bool stat, params Type[] args)
        {
            var method = owner.DefineMethod(name, MethodAttributes.Public | (stat ? MethodAttributes.Static : 0), typeof(void), args);
            method.GetILGenerator().Emit(OpCodes.Ret);
        }
        var info = module.DefineType("flir.enemymarkers.PlayerInfo", TypeAttributes.Public);
        info.DefineField("Marker", typeof(GameObject), FieldAttributes.Public); var infoType = info.CreateType();
        var component = module.DefineType("flir.enemymarkers.EmComponent", TypeAttributes.Public, typeof(MonoBehaviour));
        var billboard = component.DefineNestedType("Billboard", TypeAttributes.NestedPrivate, typeof(MonoBehaviour));
        billboard.DefineField("_mainCamera", brokenCamera ? typeof(object) : typeof(Camera), FieldAttributes.Private);
        Method(billboard, "LateUpdate", false); billboard.CreateType();
        component.DefineField("_gameWorld", typeof(GameWorld), FieldAttributes.Private);
        component.DefineField("_markersDict", typeof(Dictionary<,>).MakeGenericType(typeof(IPlayer), infoType), FieldAttributes.Private);
        Method(component, "OnPersonAdd", false, typeof(IPlayer)); Method(component, "PlayerDeadOrUnspawn", false, typeof(IPlayer));
        Method(component, "InitializeMarkers", false);
        Method(component, "OnDestroy", false, brokenDestroy ? new[] { typeof(IPlayer) } : Type.EmptyTypes);
        component.CreateType();
        var hook = module.DefineType("NewGamePatch", TypeAttributes.Public);
        Method(hook, "PatchPrefix", true); hook.CreateType();
        var plugin = module.DefineType("flir.enemymarkers.Plugin", TypeAttributes.Public, typeof(BaseUnityPlugin));
        plugin.SetCustomAttribute(new CustomAttributeBuilder(typeof(BepInPlugin).GetConstructor([typeof(string), typeof(string), typeof(string)]),
            ["flir.enemymarkers", "Enemy Markers", "1.4.1"]));
        return plugin.CreateType();
    }
}
