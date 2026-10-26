using Mono.Cecil;
using System.Security.Cryptography;

internal static class PublishedMetadata
{
    internal static void Check(string dllPath, string gamePath)
    {
        using var dll = AssemblyDefinition.ReadAssembly(dllPath);
        using var hash = SHA256.Create();
        Program.Check(BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(dllPath))).Replace("-", "") == "0BAD292DA16386DE8A737C5C627016030A343256A3670823ABEE805439CB75BD", "exact reviewed Amand release bytes");
        var plugin = dll.MainModule.GetType("AmandsGraphics.AmandsGraphicsPlugin");
        var meta = plugin.CustomAttributes.Single(x => x.AttributeType.FullName == "BepInEx.BepInPlugin");
        Program.Check((string)meta.ConstructorArguments[0].Value == "com.Amanda.Graphics" && (string)meta.ConstructorArguments[2].Value == "1.8.0", "released BepInPlugin identity");
        Program.Check(dll.Name.Name == "AmandsGraphics" && dll.Name.Version == new Version(1, 8, 0, 0), "released assembly identity");
        var type = dll.MainModule.GetType("AmandsGraphics.AmandsGraphicsClass");
        var activation = type.Methods.Single(m => m.Name == "ActivateAmandsGraphics");
        Program.Check(!activation.IsStatic && activation.ReturnType.FullName == "System.Void"
            && activation.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(new[] { "UnityEngine.GameObject", "PrismEffects" }), "released activation API");
        var instructions = activation.Body.Instructions;
        Program.Check(instructions.Any(i => i.Operand is MethodReference mr && mr.Name == "UpdateAmandsGraphics"), "released native activation reaches intercepted update");
        foreach (var name in new[] { "FPSCamera", "FPSCameraCamera", "FPSCameraPrismEffects", "levelSettings", "scene", "registeredLights", "registeredVolumetricLights" })
            Program.Check(type.Fields.Any(f => f.Name == name && f.IsStatic && !f.IsInitOnly), "released writable static contract " + name);
        foreach (var name in new[] { "defaultLightsUseLinearIntensity", "defaultZeroLevel", "defaultSkyColor", "defaultEquatorColor", "defaultGroundColor", "defaultNightVisionSkyColor", "defaultNightVisionEquatorColor", "defaultNightVisionGroundColor", "defaultGradientColorKeys", "defaultAmbientBrightness", "defaultAmbientContrast", "defaultLightIntensity", "defaultSunMeshBrightness", "defaultScatteringBrightnessMultiplier" })
        {
            Program.Check(type.Fields.Any(f => f.Name == name && f.IsStatic), "released world baseline " + name);
            Program.Check(instructions.Any(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Stsfld && i.Operand is FieldReference f && f.Name == name), "native activation recaptures baseline " + name);
        }
        var patch = dll.MainModule.GetType("AmandsGraphics.Patches.AmandsGraphicsPrismEffectsPatch").Methods.Single(m => m.Name == "PatchPostFix");
        Program.Check(patch.Body.Instructions.Any(i => Equals(i.Operand, "FPS Camera")), "released Prism hook requires exact final camera name");
        Program.Check(patch.Body.Instructions.Any(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Stfld && i.Operand is FieldReference f && f.Name == "GraphicsMode"), "released Prism hook changes mode before asynchronous activation");
        using var game = AssemblyDefinition.ReadAssembly(gamePath);
        var manager = game.MainModule.GetType("EFT.CameraControl.CameraManager");
        var create = manager.Methods.Single(m => m.Name == "SetCameraFromPrefab");
        var instantiate = create.Body.Instructions.First(i => i.Operand is MethodReference mr && mr.Name == "Instantiate");
        var rename = create.Body.Instructions.First(i => Equals(i.Operand, "FPS Camera"));
        Program.Check(instantiate.Offset < rename.Offset, "actual EFT instantiates before renaming FPS Camera");
        var construct = game.MainModule.GetType("EFT.CameraControl.PlayerCameraController").Methods.Single(m => m.Name == "Construct");
        Program.Check(construct.Body.Instructions.Any(i => i.Operand is MethodReference mr && mr.Name == "SetCameraFromSettings"), "actual EFT player camera creation rebuilds camera from settings");
    }
}
