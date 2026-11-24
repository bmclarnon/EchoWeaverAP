// SPDX-License-Identifier: BSD-3-Clause
// Copyright (c) 2026 The EchoWeaverAP Authors

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EchoWeaverAP.Randomizer;
using HarmonyLib;

namespace EchoWeaverAP.Patches;

/// <summary>
/// Harmony patches to reset Archipelago data when save data is reset.
/// </summary>
public class ResetDataPatcher
{
    [HarmonyPatch]
    public static class SettingsOverlay_moveToSubLevel_Patch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            return typeof(SettingsOverlay)
                .GetNestedTypes(AccessTools.all)
                .Where(t => t.Name.Contains("<>c"))
                .SelectMany(t => t.GetMethods(AccessTools.all))
                .Where(m => m.Name.StartsWith("<moveToSubLevel>b__"));
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codeMatcher = new CodeMatcher(instructions);
            codeMatcher.MatchForward(true,
                new CodeMatch(OpCodes.Newobj, AccessTools.Constructor(typeof(PlayerData))),
                new CodeMatch(OpCodes.Call, AccessTools.PropertySetter(typeof(PlayerData), nameof(PlayerData.Instance))));
            if (codeMatcher.IsValid)
            {
                codeMatcher.InsertAndAdvance(
                    new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(Plugin), nameof(Plugin.Randomizer))),
                    new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(IRandomizer), nameof(IRandomizer.Reset))));
            }
            return codeMatcher.Instructions();
        }
    }
}