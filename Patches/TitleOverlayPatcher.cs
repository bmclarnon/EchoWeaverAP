// SPDX-License-Identifier: BSD-3-Clause
// Copyright (c) 2026 The EchoWeaverAP Authors

using System.Collections.Generic;
using System.Reflection.Emit;
using EchoWeaverAP.Randomizer;
using geode;
using HarmonyLib;
using UnityEngine;

namespace EchoWeaverAP.Patches;

/// <summary>
/// Harmony patches to enable Randomized configuration on the title screen.
/// </summary>
public class TitleOverlayPatcher
{
    /// <summary>
    /// Prevents the game from starting until the Randomizer is ready.
    /// </summary>
    [HarmonyPatch(typeof(TitleOverlay))]
    [HarmonyPatch(nameof(TitleOverlay.PlayGame))]
    public static class TitleOverlay_PlayGame_Patch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codeMatcher = new CodeMatcher(instructions);
            codeMatcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldarg_0),
                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(geode.BaseOverlay), "allowedToClick")),
                new CodeMatch(i => i.opcode == OpCodes.Brfalse || i.opcode == OpCodes.Brfalse_S, name: "branch"))
            .ThrowIfInvalid("Failed to find `!this.allowedToClick`")
            .InsertAndAdvance(
                new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(Plugin), nameof(Plugin.Randomizer))),
                new CodeInstruction(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(IRandomizer), nameof(IRandomizer.Initialized))),
                new CodeInstruction(OpCodes.Brfalse, codeMatcher.NamedMatch("branch").operand));
            return codeMatcher.Instructions();
        }
    }

    /// <summary>
    /// Grays out the "Play" option when the Randomizer isn't initialized.
    /// </summary>
    [HarmonyPatch(typeof(TitleOverlay))]
    [HarmonyPatch("updateButtonVisualForSelectionChange")]
    public static class TitleOverlay_updateButtonVisualForSelectionChange_Patch
    {
        private static readonly AccessTools.FieldRef<TitleOverlay, RectTransform> s_buttonBackingRef =
            AccessTools.FieldRefAccess<TitleOverlay, RectTransform>("buttonBacking");

        private static readonly Color Disabled = new(127f / 255, 127f / 12, 127f / 78);

        static void Postfix(TitleOverlay __instance, GameObject _newSelection)
        {
            s_buttonBackingRef(__instance)?.GetComponent<CanvasRenderer>()?.SetColor(
                _newSelection.name == "play" && !Plugin.Randomizer.Initialized ? Disabled : Color.white);
        }
    }

    /// <summary>
    /// Restores the color of the "Play" option when the Randomizer is initialized.
    /// </summary>
    // Patching Awake ensures callbacks are registered after initialization is complete.
    [HarmonyPatch(typeof(GameManager))]
    [HarmonyPatch("Awake")]
    public static class GameManager_Awake_Patch
    {
        private static readonly AccessTools.FieldRef<OverlayManager> s_overlayManagerRef =
            AccessTools.StaticFieldRefAccess<OverlayManager>(AccessTools.Field(typeof(OverlayManager), "instance"));
        private static readonly AccessTools.FieldRef<OverlayManager, List<BaseOverlay>> s_activeOverlaysRef =
            AccessTools.FieldRefAccess<OverlayManager, List<BaseOverlay>>("activeOverlays");
        private static readonly AccessTools.FieldRef<TitleOverlay, RectTransform> s_buttonBackingRef =
            AccessTools.FieldRefAccess<TitleOverlay, RectTransform>("buttonBacking");

        static void Postfix()
        {
            Plugin.Randomizer.OnInitialized += ResetPlayColor;
        }

        static void ResetPlayColor()
        {
            OverlayManager overlayManager = s_overlayManagerRef();
            if (overlayManager != null)
            {
                foreach (BaseOverlay baseOverlay in s_activeOverlaysRef(overlayManager))
                {
                    if (baseOverlay is TitleOverlay overlay)
                    {
                        s_buttonBackingRef(overlay)?.GetComponent<CanvasRenderer>()?.SetColor(Color.white);
                    }
                }
            }
        }
    }
}
