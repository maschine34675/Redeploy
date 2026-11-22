using Mono.Cecil;

internal static class Published
{
    internal static void Verify(string path)
    {
        using var assembly=AssemblyDefinition.ReadAssembly(path);
        if(assembly.Name.Name!="spt-realism-common-lib") throw new Exception("assembly identity");
        var plugin=assembly.MainModule.GetType("RealismCommonLib.Plugin");
        var metadata=plugin.CustomAttributes.Single(a=>a.AttributeType.FullName=="BepInEx.BepInPlugin");
        if((string)metadata.ConstructorArguments[0].Value!="com.fontaine.spt-realism-common-lib"
            || (string)metadata.ConstructorArguments[2].Value!="0.1.0") throw new Exception("plugin metadata");
        string[] slots={"PlayerStateInstance","WeaponStateInstance","AimStateInstance","HealthStateInstance","HazardsStateInstance",
            "GearStateInstance","FiringStateInstance","SkillStateInstance","ReloadStateInstance"};
        foreach(string slot in slots)
        {
            var field=plugin.Fields.Single(f=>f.Name==slot&&f.IsStatic);
            var type=assembly.MainModule.GetType(field.FieldType.FullName);
            if(type.BaseType.FullName!="UnityEngine.MonoBehaviour") throw new Exception("controller base");
            foreach(var method in type.Methods.Where(m=>!m.IsStatic&&!m.IsSpecialName&&!m.IsConstructor&&m.Name!="Awake"))
                if(method.ReturnType.FullName!="System.Void") throw new Exception("unknown nonvoid callback: "+method.FullName);
        }
        var init=plugin.Methods.Single(m=>m.Name=="InitPlayerComponents");
        if(init.Parameters.Count!=1||init.Parameters[0].ParameterType.FullName!="EFT.Player") throw new Exception("init owner");
        foreach(string name in new[]{"PlayerState","WeaponState","GearState","FiringState"})
        {
            var type=assembly.MainModule.GetType("RealismCommonLib.StateControllers.InstanceState."+name);
            var destroy=type.Methods.SingleOrDefault(m=>m.Name=="OnDestroy");
            if(destroy==null) continue;
            foreach(var instruction in destroy.Body.Instructions)
                if((instruction.OpCode == Mono.Cecil.Cil.OpCodes.Call || instruction.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt)
                    && instruction.Operand is MethodReference method && !method.Name.StartsWith("remove_")
                    && !method.Name.StartsWith(".ctor") && method.Name!="Remove")
                    throw new Exception("unreviewed destroy side effect: "+method.FullName);
        }
        Console.WriteLine("COMMONLIB_PUBLISHED_METADATA_PASSED");
    }
}
