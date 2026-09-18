using HarmonyLib;
using Redeploy.Core.Compat;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace Redeploy.Tests;

/// <summary>
/// The generalised instruction-rewriting matcher (declaring type + method name + generic
/// argument, optional castclass), driven directly against synthetic <see cref="CodeInstruction"/>
/// lists — same approach as FindObjectOfTypeRewriteTests, extended with the declaring-type
/// dimension that distinguishes e.g. FindObjectsProxy.FindUnityObjectsOfType&lt;T&gt;() from
/// UnityEngine.Object.FindObjectOfType&lt;T&gt;() (same name shape, different owner).
/// </summary>
public class GenericStaticCallRewriteTests
{
    private sealed class TargetType
    {
    }

    private sealed class OtherType
    {
    }

    // Stand-in for FindObjectsProxy: a static generic zero-argument method, plus a same-named
    // overload with a parameter, on ITS OWN declaring type.
    private static class FakeProxy
    {
        public static object FindUnityObjectsOfType<T>() => null;
        public static object FindUnityObjectsOfType<T>(bool includeInactive) => null;
    }

    // A second declaring type with an identically-shaped, identically-named method: proves the
    // matcher tells the two apart instead of matching by name/shape alone.
    private static class OtherDeclaringType
    {
        public static object FindUnityObjectsOfType<T>() => null;
    }

    private static class FakeReplacement
    {
        public static object Resolve() => null;
        public static object ResolveTyped() => null;
    }

    private static readonly MethodInfo OpenCall =
        typeof(FakeProxy).GetMethods().Single(m => m.Name == "FindUnityObjectsOfType" && m.GetParameters().Length == 0);

    private static readonly MethodInfo OpenCallWithParameter =
        typeof(FakeProxy).GetMethods().Single(m => m.Name == "FindUnityObjectsOfType" && m.GetParameters().Length == 1);

    private static readonly MethodInfo OpenCallOnOtherType =
        typeof(OtherDeclaringType).GetMethods().Single(m => m.Name == "FindUnityObjectsOfType");

    private static readonly MethodInfo Replacement = typeof(FakeReplacement).GetMethod(nameof(FakeReplacement.Resolve));

    private static MethodInfo CallOf<T>() => OpenCall.MakeGenericMethod(typeof(T));
    private static MethodInfo CallWithParameterOf<T>() => OpenCallWithParameter.MakeGenericMethod(typeof(T));
    private static MethodInfo CallOnOtherTypeOf<T>() => OpenCallOnOtherType.MakeGenericMethod(typeof(T));

    [Fact]
    public void OneMatchIsReplacedByACallAndACastclassWhenCastToIsGiven()
    {
        var label = new Label();
        var instructions = new List<CodeInstruction>
        {
            new(OpCodes.Nop),
            new(OpCodes.Call, CallOf<TargetType>()) { labels = { label } },
            new(OpCodes.Stloc_0),
        };

        var replaced = GenericStaticCallRewrite.TryReplace(
            instructions, typeof(FakeProxy), "FindUnityObjectsOfType", typeof(TargetType), Replacement, castTo: typeof(TargetType));

        Assert.True(replaced);
        Assert.Equal(4, instructions.Count);
        Assert.Equal(OpCodes.Nop, instructions[0].opcode);

        Assert.Equal(OpCodes.Call, instructions[1].opcode);
        Assert.Same(Replacement, instructions[1].operand);
        Assert.Contains(label, instructions[1].labels); // the call site's label must not be lost

        Assert.Equal(OpCodes.Castclass, instructions[2].opcode);
        Assert.Equal(typeof(TargetType), instructions[2].operand);

        Assert.Equal(OpCodes.Stloc_0, instructions[3].opcode);
    }

    [Fact]
    public void OneMatchIsReplacedByJustACallWhenCastToIsNull()
    {
        var instructions = new List<CodeInstruction>
        {
            new(OpCodes.Call, CallOf<TargetType>()),
            new(OpCodes.Stloc_0),
        };

        var replaced = GenericStaticCallRewrite.TryReplace(
            instructions, typeof(FakeProxy), "FindUnityObjectsOfType", typeof(TargetType), Replacement, castTo: null);

        Assert.True(replaced);
        Assert.Equal(2, instructions.Count); // no castclass inserted
        Assert.Equal(OpCodes.Call, instructions[0].opcode);
        Assert.Same(Replacement, instructions[0].operand);
        Assert.Equal(OpCodes.Stloc_0, instructions[1].opcode);
    }

    [Fact]
    public void ACallvirtMatchesToo()
    {
        var instructions = new List<CodeInstruction> { new(OpCodes.Callvirt, CallOf<TargetType>()) };

        var replaced = GenericStaticCallRewrite.TryReplace(
            instructions, typeof(FakeProxy), "FindUnityObjectsOfType", typeof(TargetType), Replacement, castTo: null);

        Assert.True(replaced);
        Assert.Equal(OpCodes.Call, instructions[0].opcode);
    }

    [Fact]
    public void ZeroMatchesLeavesTheListUnchanged()
    {
        var instructions = new List<CodeInstruction> { new(OpCodes.Nop), new(OpCodes.Ldnull), new(OpCodes.Ret) };
        var original = new List<CodeInstruction>(instructions);

        var replaced = GenericStaticCallRewrite.TryReplace(
            instructions, typeof(FakeProxy), "FindUnityObjectsOfType", typeof(TargetType), Replacement, castTo: null);

        Assert.False(replaced);
        AssertSameShape(original, instructions);
    }

    [Fact]
    public void TwoMatchesLeavesTheListUnchanged()
    {
        var instructions = new List<CodeInstruction>
        {
            new(OpCodes.Call, CallOf<TargetType>()),
            new(OpCodes.Pop),
            new(OpCodes.Call, CallOf<TargetType>()),
        };
        var original = new List<CodeInstruction>(instructions);

        var replaced = GenericStaticCallRewrite.TryReplace(
            instructions, typeof(FakeProxy), "FindUnityObjectsOfType", typeof(TargetType), Replacement, castTo: null);

        Assert.False(replaced);
        AssertSameShape(original, instructions);
        Assert.Equal(3, instructions.Count);
    }

    [Fact]
    public void AWrongDeclaringTypeDoesNotCount()
    {
        // Same method name, same generic-argument shape, wrong owner: e.g. UnityEngine.Object's
        // own FindObjectOfType<T>() must not be mistaken for FindObjectsProxy's wrapper.
        var instructions = new List<CodeInstruction> { new(OpCodes.Call, CallOnOtherTypeOf<TargetType>()) };
        var original = new List<CodeInstruction>(instructions);

        var replaced = GenericStaticCallRewrite.TryReplace(
            instructions, typeof(FakeProxy), "FindUnityObjectsOfType", typeof(TargetType), Replacement, castTo: null);

        Assert.False(replaced);
        AssertSameShape(original, instructions);
    }

    [Fact]
    public void AWrongMethodNameDoesNotCount()
    {
        var instructions = new List<CodeInstruction> { new(OpCodes.Call, CallOf<TargetType>()) };
        var original = new List<CodeInstruction>(instructions);

        var replaced = GenericStaticCallRewrite.TryReplace(
            instructions, typeof(FakeProxy), "SomeOtherMethod", typeof(TargetType), Replacement, castTo: null);

        Assert.False(replaced);
        AssertSameShape(original, instructions);
    }

    [Fact]
    public void AWrongGenericArgumentDoesNotCount()
    {
        var instructions = new List<CodeInstruction> { new(OpCodes.Call, CallOf<OtherType>()) };
        var original = new List<CodeInstruction>(instructions);

        var replaced = GenericStaticCallRewrite.TryReplace(
            instructions, typeof(FakeProxy), "FindUnityObjectsOfType", typeof(TargetType), Replacement, castTo: null);

        Assert.False(replaced);
        AssertSameShape(original, instructions);
    }

    [Fact]
    public void ACallToTheOverloadWithAParameterDoesNotCount()
    {
        var instructions = new List<CodeInstruction>
        {
            new(OpCodes.Ldc_I4_1),
            new(OpCodes.Call, CallWithParameterOf<TargetType>()),
        };
        var original = new List<CodeInstruction>(instructions);

        var replaced = GenericStaticCallRewrite.TryReplace(
            instructions, typeof(FakeProxy), "FindUnityObjectsOfType", typeof(TargetType), Replacement, castTo: null);

        Assert.False(replaced);
        AssertSameShape(original, instructions);
    }

    [Fact]
    public void MissingArgumentsAreRefusedRatherThanThrow()
    {
        var instructions = new List<CodeInstruction> { new(OpCodes.Call, CallOf<TargetType>()) };

        Assert.False(GenericStaticCallRewrite.TryReplace(null, typeof(FakeProxy), "FindUnityObjectsOfType", typeof(TargetType), Replacement, null));
        Assert.False(GenericStaticCallRewrite.TryReplace(instructions, null, "FindUnityObjectsOfType", typeof(TargetType), Replacement, null));
        Assert.False(GenericStaticCallRewrite.TryReplace(instructions, typeof(FakeProxy), null, typeof(TargetType), Replacement, null));
        Assert.False(GenericStaticCallRewrite.TryReplace(instructions, typeof(FakeProxy), "", typeof(TargetType), Replacement, null));
        Assert.False(GenericStaticCallRewrite.TryReplace(instructions, typeof(FakeProxy), "FindUnityObjectsOfType", null, Replacement, null));
        Assert.False(GenericStaticCallRewrite.TryReplace(instructions, typeof(FakeProxy), "FindUnityObjectsOfType", typeof(TargetType), null, null));
    }

    private static void AssertSameShape(List<CodeInstruction> expected, List<CodeInstruction> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].opcode, actual[i].opcode);
            Assert.Equal(expected[i].operand, actual[i].operand);
        }
    }
}
