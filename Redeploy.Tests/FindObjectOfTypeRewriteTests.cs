using HarmonyLib;
using Redeploy.Core.Compat;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace Redeploy.Tests;

/// <summary>
/// The instruction-rewriting half of the scene-search-cache transpiler, driven directly against
/// synthetic <see cref="CodeInstruction"/> lists instead of a decompiled method body. The
/// generic FindObjectOfType&lt;T&gt;() call is stood in for by a same-shaped local generic method
/// (Name == "FindObjectOfType", one generic argument), since the real UnityEngine.Object method
/// this targets needs no Unity runtime to identify — MethodInfo and Type are enough.
/// </summary>
public class FindObjectOfTypeRewriteTests
{
    private sealed class TargetType
    {
    }

    private sealed class OtherType
    {
    }

    private static class FakeUnityObject
    {
        // Stand-in for UnityEngine.Object.FindObjectOfType<T>(): same name, one generic argument.
        public static object FindObjectOfType<T>() => null;

        // Stand-in for the other overload Unity ships, UnityEngine.Object.FindObjectOfType<T>(bool
        // includeInactive): same name, same one-generic-argument shape, but a parameter the
        // zero-argument replacement call cannot account for.
        public static object FindObjectOfType<T>(bool includeInactive) => null;
    }

    private static class FakeReplacement
    {
        public static object Resolve() => null;
    }

    private static readonly MethodInfo OpenFindObjectOfType =
        typeof(FakeUnityObject).GetMethods()
            .Single(m => m.Name == nameof(FakeUnityObject.FindObjectOfType) && m.GetParameters().Length == 0);

    private static readonly MethodInfo OpenFindObjectOfTypeIncludingInactive =
        typeof(FakeUnityObject).GetMethods()
            .Single(m => m.Name == nameof(FakeUnityObject.FindObjectOfType) && m.GetParameters().Length == 1);

    private static readonly MethodInfo Replacement =
        typeof(FakeReplacement).GetMethod(nameof(FakeReplacement.Resolve));

    private static MethodInfo FindObjectOfTypeOf<T>() => OpenFindObjectOfType.MakeGenericMethod(typeof(T));

    private static MethodInfo FindObjectOfTypeIncludingInactiveOf<T>() =>
        OpenFindObjectOfTypeIncludingInactive.MakeGenericMethod(typeof(T));

    [Fact]
    public void OneMatchIsReplacedByACallAndACastclass()
    {
        var label = new Label();
        var instructions = new List<CodeInstruction>
        {
            new(OpCodes.Nop),
            new(OpCodes.Call, FindObjectOfTypeOf<TargetType>()) { labels = { label } },
            new(OpCodes.Stloc_0),
        };

        var replaced = FindObjectOfTypeRewrite.TryReplace(instructions, typeof(TargetType), Replacement);

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
    public void ACallvirtMatchesToo()
    {
        var instructions = new List<CodeInstruction>
        {
            new(OpCodes.Callvirt, FindObjectOfTypeOf<TargetType>()),
        };

        var replaced = FindObjectOfTypeRewrite.TryReplace(instructions, typeof(TargetType), Replacement);

        Assert.True(replaced);
        Assert.Equal(OpCodes.Call, instructions[0].opcode);
        Assert.Equal(OpCodes.Castclass, instructions[1].opcode);
    }

    [Fact]
    public void ZeroMatchesLeavesTheListUnchanged()
    {
        var instructions = new List<CodeInstruction>
        {
            new(OpCodes.Nop),
            new(OpCodes.Ldnull),
            new(OpCodes.Ret),
        };
        var original = new List<CodeInstruction>(instructions);

        var replaced = FindObjectOfTypeRewrite.TryReplace(instructions, typeof(TargetType), Replacement);

        Assert.False(replaced);
        AssertSameShape(original, instructions);
    }

    [Fact]
    public void ACallToTheWrongGenericArgumentDoesNotCount()
    {
        var instructions = new List<CodeInstruction>
        {
            new(OpCodes.Call, FindObjectOfTypeOf<OtherType>()),
        };
        var original = new List<CodeInstruction>(instructions);

        var replaced = FindObjectOfTypeRewrite.TryReplace(instructions, typeof(TargetType), Replacement);

        Assert.False(replaced);
        AssertSameShape(original, instructions);
    }

    [Fact]
    public void ACallToTheOverloadWithAnArgumentDoesNotCount()
    {
        // Same name, same single generic argument as the zero-argument overload this targets —
        // but the replacement call is zero-argument, so matching this one would strand its bool
        // argument's push instruction under the replacement's return value (see IsFindObjectOfTypeCall).
        var instructions = new List<CodeInstruction>
        {
            new(OpCodes.Ldc_I4_1),
            new(OpCodes.Call, FindObjectOfTypeIncludingInactiveOf<TargetType>()),
        };
        var original = new List<CodeInstruction>(instructions);

        var replaced = FindObjectOfTypeRewrite.TryReplace(instructions, typeof(TargetType), Replacement);

        Assert.False(replaced);
        AssertSameShape(original, instructions);
    }

    [Fact]
    public void TwoMatchesLeavesTheListUnchanged()
    {
        var instructions = new List<CodeInstruction>
        {
            new(OpCodes.Call, FindObjectOfTypeOf<TargetType>()),
            new(OpCodes.Pop),
            new(OpCodes.Call, FindObjectOfTypeOf<TargetType>()),
        };
        var original = new List<CodeInstruction>(instructions);

        var replaced = FindObjectOfTypeRewrite.TryReplace(instructions, typeof(TargetType), Replacement);

        Assert.False(replaced);
        AssertSameShape(original, instructions);
        Assert.Equal(3, instructions.Count);
    }

    [Fact]
    public void MissingArgumentsAreRefusedRatherThanThrow()
    {
        var instructions = new List<CodeInstruction> { new(OpCodes.Call, FindObjectOfTypeOf<TargetType>()) };

        Assert.False(FindObjectOfTypeRewrite.TryReplace(null, typeof(TargetType), Replacement));
        Assert.False(FindObjectOfTypeRewrite.TryReplace(instructions, null, Replacement));
        Assert.False(FindObjectOfTypeRewrite.TryReplace(instructions, typeof(TargetType), null));
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
