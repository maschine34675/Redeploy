using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Redeploy.Core.Compat;

/// <summary>
/// Generalises <see cref="FindObjectOfTypeRewrite"/>'s single-call-site rewrite to any generic,
/// zero-parameter static call, matched by declaring type + method name + the one generic argument
/// instead of just name + generic argument. Added for
/// FindObjectsProxy.FindUnityObjectsOfType&lt;OcclusionCullingSwitcher&gt;()
/// (Compat/OcclusionCullingFix.cs, Core 1.0.6) without touching FindObjectOfTypeRewrite itself:
/// that class's matching never checked the declaring type (nothing needed it while
/// UnityEngine.Object.FindObjectOfType&lt;T&gt;() was the only target), and its tests build a
/// stand-in method on an unrelated class to prove that — adding a declaring-type check there would
/// have failed them. A sibling keeps that file, and FindObjectOfTypeRewriteTests, byte-for-byte.
///
/// Same replacement shape as FindObjectOfTypeRewrite: the matched call becomes a call to
/// <c>replacement</c>, optionally followed by a castclass. FindObjectOfTypeRewrite always needs
/// the cast, because CullingGridPreProcessCache.Resolve() returns UnityEngine.Object, not the
/// specific T the matched call pushed. Here <paramref name="castTo"/> can be null: when the
/// replacement's return type already is what the matched call would have pushed (e.g.
/// OcclusionCullingSwitcherCache.Resolve() returns OcclusionCullingSwitcher[], exactly what
/// FindUnityObjectsOfType&lt;OcclusionCullingSwitcher&gt;() returns), nothing needs converting and
/// a castclass would just be a wasted instruction.
///
/// Touches nothing and returns false unless the pattern occurs exactly once, same reasoning as
/// FindObjectOfTypeRewrite (compare QuietBrainLog's PmcBrainLogPatch).
///
/// Pure (Harmony's CodeInstruction and reflection's MethodInfo/Type need no Unity runtime to
/// exist), so Redeploy.Tests links this file and drives it with synthetic instruction lists.
/// </summary>
internal static class GenericStaticCallRewrite
{
    public static bool TryReplace(
        List<CodeInstruction> instructions,
        Type declaringType,
        string methodName,
        Type genericArgument,
        MethodInfo replacement,
        Type castTo)
    {
        if (instructions == null || declaringType == null || string.IsNullOrEmpty(methodName)
            || genericArgument == null || replacement == null)
            return false;

        var index = -1;
        var hits = 0;

        for (var i = 0; i < instructions.Count; i++)
        {
            if (!IsMatch(instructions[i], declaringType, methodName, genericArgument))
                continue;

            if (hits == 0)
                index = i;
            hits++;
        }

        if (hits != 1)
            return false;

        // Copy-constructed so the instruction keeps its labels: a branch could land on this
        // exact call site, and losing the label would misroute it.
        instructions[index] = new CodeInstruction(instructions[index]) { opcode = OpCodes.Call, operand = replacement };
        if (castTo != null)
            instructions.Insert(index + 1, new CodeInstruction(OpCodes.Castclass, castTo));
        return true;
    }

    private static bool IsMatch(CodeInstruction instruction, Type declaringType, string methodName, Type genericArgument)
    {
        if (instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt)
            return false;

        if (instruction.operand is not MethodInfo method || !method.IsGenericMethod)
            return false;

        if (method.DeclaringType != declaringType)
            return false;

        if (method.Name != methodName)
            return false;

        // Same reasoning as FindObjectOfTypeRewrite.IsFindObjectOfTypeCall: a same-named,
        // same-generic-argument overload that takes a parameter (e.g. Unity's
        // FindObjectOfType<T>(bool includeInactive)) would strand that argument's push instruction
        // under the replacement's return value once the zero-argument replacement call goes in — a
        // stack shape the CLR verifier rejects at JIT time. Requiring zero parameters sends that
        // overload down the "not found" path instead, same as a wrong generic argument.
        if (method.GetParameters().Length != 0)
            return false;

        var typeArgs = method.GetGenericArguments();
        return typeArgs.Length == 1 && typeArgs[0] == genericArgument;
    }
}
