using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Redeploy.Core.Compat;

/// <summary>
/// Rewrites a compiled method's instructions in place, replacing a single call to
/// UnityEngine.Object.FindObjectOfType&lt;T&gt;() (T == <c>searchType</c>) with a call to a
/// replacement method that returns UnityEngine.Object, followed by a castclass to
/// <c>searchType</c> — the same zero-argument, one-value stack shape the generic call had, so
/// nothing around the call site has to move.
///
/// Touches nothing and returns false unless the pattern occurs exactly once: zero means the
/// game changed and there is nothing to fix, more than one means guessing which call site is
/// the expensive one would be a worse bug than the one this exists to fix (compare
/// QuietBrainLog's PmcBrainLogPatch, same rule for the same reason).
///
/// Pure (Harmony's CodeInstruction and reflection's MethodInfo/Type need no Unity runtime to
/// exist), so Redeploy.Tests links this file and drives it with synthetic instruction lists
/// instead of a decompiled method body.
/// </summary>
internal static class FindObjectOfTypeRewrite
{
    public static bool TryReplace(List<CodeInstruction> instructions, Type searchType, MethodInfo replacement)
    {
        if (instructions == null || searchType == null || replacement == null)
            return false;

        var index = -1;
        var hits = 0;

        for (var i = 0; i < instructions.Count; i++)
        {
            if (!IsFindObjectOfTypeCall(instructions[i], searchType))
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
        instructions.Insert(index + 1, new CodeInstruction(OpCodes.Castclass, searchType));
        return true;
    }

    private static bool IsFindObjectOfTypeCall(CodeInstruction instruction, Type searchType)
    {
        if (instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt)
            return false;

        if (instruction.operand is not MethodInfo method || !method.IsGenericMethod)
            return false;

        if (method.Name != "FindObjectOfType")
            return false;

        // The replacement is a zero-argument static call (CullingGridPreProcessCache.Resolve()):
        // it assumes the call it replaces pushed nothing but its own return value. Unity also
        // ships FindObjectOfType<T>(bool includeInactive), same name and same one-generic-argument
        // shape — matching that overload here would leave its bool argument's push instruction
        // stranded under the replacement call's return value, an IL stack shape the CLR verifier
        // rejects at JIT time. Requiring zero parameters sends that overload down the "not found"
        // path instead, same as a wrong generic argument.
        if (method.GetParameters().Length != 0)
            return false;

        var typeArgs = method.GetGenericArguments();
        return typeArgs.Length == 1 && typeArgs[0] == searchType;
    }
}
