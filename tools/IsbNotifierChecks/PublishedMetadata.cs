using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;

internal static class PublishedMetadata
{
    internal static void Check(string path)
    {
        using var hash = SHA256.Create();
        Program.Check(BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "") == "1DBFD16EB0F498992A06D075766FA79EE5F5BE66526FCAA00A4E9F3814B9F3DA", "exact reviewed 2.0.4 notifier bytes");
        using var dll = AssemblyDefinition.ReadAssembly(path);
        var type = dll.MainModule.GetType("ISBInfo.ISBInfoPlugin");
        var metadata = type.CustomAttributes.Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
        Program.Check((string)metadata.ConstructorArguments[0].Value == "com.samc137.ISBinfo" && (string)metadata.ConstructorArguments[2].Value == "2.0.4", "real independent notifier GUID/version");
        Program.Check(dll.Name.Name == "ISBNotify" && dll.Name.Version == new Version(2, 0, 4, 0), "real assembly identity");
        foreach (var name in new[] { "LocalRaidPresentationEnded", "LocalPlayerObservedInRaid", "NextPresentationStateRefreshTime", "EnablePlugin", "Instance" })
        {
            var field = type.Fields.Single(f => f.Name == name);
            Program.Check(field.IsStatic && field.IsPrivate && !field.IsInitOnly, "private writable static field " + name);
        }
        foreach (var name in new[] { "RefreshLocalRaidPresentationState", "CanPresentLocally", "IsPluginEnabled", "IsHeadless", "ResetState", "MarkLocalRaidPresentationEnded" })
        {
            var method = type.Methods.Single(m => m.Name == name);
            Program.Check(method.IsStatic && method.Parameters.Count == 0 && (name == "MarkLocalRaidPresentationEnded" ? method.IsPublic : method.IsPrivate), "native method contract " + name);
        }
        var refresh = type.Methods.Single(m => m.Name == "RefreshLocalRaidPresentationState");
        Program.Check(refresh.Body.Instructions[0].OpCode.Code == Code.Ldsfld && ((FieldReference)refresh.Body.Instructions[0].Operand).Name == "LocalRaidPresentationEnded", "release refresh immediately reads latched end state");
        Program.Check(refresh.Body.Instructions.Any(i => i.OpCode.Code == Code.Bne_Un_S && i.Previous?.OpCode.Code == Code.Ldloc_2
            && i.Previous.Previous?.Operand is MethodReference m && m.Name == "get_Current"), "release alive membership uses physical reference comparison");
        Program.Check(refresh.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "AllAlivePlayersList"), "release uses AllAlivePlayersList");
        var writes = type.Methods.Where(m => m.HasBody && m.Body.Instructions.Any(i => i.OpCode.Code == Code.Stsfld && i.Operand is FieldReference f && f.Name == "LocalRaidPresentationEnded")).Select(m => m.Name).OrderBy(n => n);
        Program.Check(writes.SequenceEqual(new[] { "MarkLocalRaidPresentationEnded", "RefreshLocalRaidPresentationState", "ResetState" }), "only known lifecycle writers own the release latch");
        var reset = type.Methods.Single(m => m.Name == "ResetState");
        Program.Check(reset.Body.Instructions.Count(i => i.Operand is MethodReference m && m.Name == "Clear") >= 4, "broad reset clears unrelated native history and must not be used");
    }
}
