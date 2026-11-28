// SPDX-License-Identifier: BSD-3-Clause
// Copyright (c) 2026 The EchoWeaverAP Authors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using EchoWeaverAP.Randomizer;
using geode;
using HarmonyLib;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UI;

namespace EchoWeaverAP.Patches;

/// <summary>
/// Harmony patches to randomize keypad and frequency puzzles.
/// </summary>
public class PuzzlePatcher
{
    /// <summary>
    /// Updates keypad codes with solutions from the Randomizer.
    /// </summary>
    [HarmonyPatch(typeof(KeypadOverlay))]
    [HarmonyPatch(nameof(KeypadOverlay.Open))]
    public static class KeypadOverlay_Open_Patch
    {
        private static readonly AccessTools.FieldRef<KeypadInjector, List<int>[]> s_passcodesRef =
            AccessTools.FieldRefAccess<KeypadInjector, List<int>[]>("passcodes");

        static void Prefix(KeypadInjector _keypadInjector)
        {
            if (Plugin.Randomizer.TryGetKeypadInfo(_keypadInjector.InstanceID, out var passcodes, out var buttons))
            {
                s_passcodesRef(_keypadInjector) = passcodes;
            }
        }
    }

    /// <summary>
    /// Updates keypad codes with images from the Randomizer.
    /// </summary>
    [HarmonyPatch(typeof(KeypadOverlay))]
    [HarmonyPatch("Awake")]
    public static class KeypadOverlay_Awake_Patch
    {
        private static readonly AccessTools.FieldRef<KeypadInstance> s_keypadInstanceRef =
            AccessTools.StaticFieldRefAccess<KeypadInstance>(AccessTools.Field(typeof(KeypadOverlay), "instanceID"));
        private static readonly AccessTools.FieldRef<KeypadOverlay, GameObject> s_churchControlsGroupRef =
            AccessTools.FieldRefAccess<KeypadOverlay, GameObject>("churchControlsGroup");

        private delegate GameObject GetSymbolGroupForIdDelegate(KeypadOverlay keypadOverlay);
        private static readonly GetSymbolGroupForIdDelegate s_getSymbolGroupForId =
            AccessTools.MethodDelegate<GetSymbolGroupForIdDelegate>(AccessTools.Method(typeof(KeypadOverlay), "getSymbolGroupForID"));
        private static readonly GetSymbolGroupForIdDelegate s_getBigSymbolGroupForId =
            AccessTools.MethodDelegate<GetSymbolGroupForIdDelegate>(AccessTools.Method(typeof(KeypadOverlay), "getBigSymbolGroupForID"));

        private static Dictionary<Digit, Sprite> s_digitCache = null;

        static void Postfix(KeypadOverlay __instance)
        {
            KeypadInstance keypadInstance = s_keypadInstanceRef();
            if (Plugin.Randomizer.TryGetKeypadInfo(keypadInstance, out var passcodes, out var buttons))
            {
                s_digitCache ??= PopulateDigitCache();

                int index = -1;
                GameObject symbolGroup = GetActiveSymbolGroup(__instance, keypadInstance);
                foreach (Image image in symbolGroup.GetComponentsInChildren<Image>(true))
                {
                    if (!image.sprite.name.StartsWith("Digit_")) continue;
                    if (image.name != "fake-image" && ++index >= buttons.Length)
                    {
                        throw new InvalidOperationException($"Randomizer provided too few buttons for {keypadInstance}");
                    }

                    (Digit digit, D8Symmetry symmetry) = buttons[index];
                    if (keypadInstance != KeypadInstance.ChurchElevator || index != 14)
                    {
                        image.color = image.color.SetAlpha(1f);
                        image.enabled = true;
                    }
                    image.sprite = s_digitCache[digit];
                    switch (symmetry)
                    {
                        case D8Symmetry.Identity:
                            image.transform.localScale = new Vector3(1f, 1f, 1f);
                            image.transform.localEulerAngles = Vector3.zero;
                            break;

                        case D8Symmetry.R90:
                            image.transform.localScale = new Vector3(1f, 1f, 1f);
                            image.transform.localEulerAngles = new Vector3(0f, 0f, 90f);
                            break;

                        case D8Symmetry.R180:
                            image.transform.localScale = new Vector3(1f, 1f, 1f);
                            image.transform.localEulerAngles = new Vector3(0f, 0f, 180f);
                            break;

                        case D8Symmetry.R270:
                            image.transform.localScale = new Vector3(1f, 1f, 1f);
                            image.transform.localEulerAngles = new Vector3(0f, 0f, 270f);
                            break;

                        case D8Symmetry.S:
                            image.transform.localScale = new Vector3(-1f, 1f, 1f);
                            image.transform.localEulerAngles = Vector3.zero;
                            break;

                        case D8Symmetry.SR90:
                            image.transform.localScale = new Vector3(-1f, 1f, 1f);
                            image.transform.localEulerAngles = new Vector3(0f, 0f, 90f);
                            break;

                        case D8Symmetry.SR180:
                            image.transform.localScale = new Vector3(-1f, 1f, 1f);
                            image.transform.localEulerAngles = new Vector3(0f, 0f, 180f);
                            break;

                        case D8Symmetry.SR270:
                            image.transform.localScale = new Vector3(-1f, 1f, 1f);
                            image.transform.localEulerAngles = new Vector3(0f, 0f, 270f);
                            break;
                    }
                }
                if (index != buttons.Length - 1)
                {
                    throw new InvalidOperationException($"Randomizer provided too many buttons for {keypadInstance}");
                }
            }
        }

        private static Dictionary<Digit, Sprite> PopulateDigitCache()
        {
            SpriteAtlas atlas = Resources.FindObjectsOfTypeAll<SpriteAtlas>().First(a => a.name == "Common UI Atlas");
            return new()
            {
                [Digit.Zero] = atlas.GetSprite("Digit_00"),
                [Digit.One] = atlas.GetSprite("Digit_01"),
                [Digit.Two] = atlas.GetSprite("Digit_02"),
                [Digit.Three] = atlas.GetSprite("Digit_03"),
                [Digit.Four] = atlas.GetSprite("Digit_04"),
                [Digit.Five] = atlas.GetSprite("Digit_05"),
                [Digit.Six] = atlas.GetSprite("Digit_06"),
                [Digit.Seven] = atlas.GetSprite("Digit_07"),
                [Digit.Eight] = atlas.GetSprite("Digit_08"),
                [Digit.Nine] = atlas.GetSprite("Digit_09"),
                [Digit.Ten] = atlas.GetSprite("Digit_10"),
                [Digit.Eleven] = atlas.GetSprite("Digit_11"),
                [Digit.Start] = atlas.GetSprite("Digit_Start"),
                [Digit.NineClosed] = atlas.GetSprite("Digit_09_closed"),
                [Digit.SimpleStraight] = atlas.GetSprite("Digit_Simple_Straight"),
                [Digit.SimpleTurn] = atlas.GetSprite("Digit_Simple_Turn"),
            };
        }

        private static GameObject GetActiveSymbolGroup(KeypadOverlay keypadOverlay, KeypadInstance keypadInstance)
        {
            var instance = keypadInstance;  // Suppress incorrect Harmony003 warning.
            if (instance == KeypadInstance.ChurchElevator)
            {
                return s_churchControlsGroupRef(keypadOverlay);
            }
            else if (instance.ToString().Contains("BigLayout"))
            {
                return s_getBigSymbolGroupForId.Invoke(keypadOverlay);
            }
            else
            {
                return s_getSymbolGroupForId.Invoke(keypadOverlay);
            }
        }
    }

    /// <summary>
    /// Updates the church elevator to use the codes from the Randomizer.
    /// </summary>
    [HarmonyPatch(typeof(ChurchElevatorControlTerminal))]
    [HarmonyPatch("onInteract")]
    public static class ChurchElevatorControlTerminal_onInteract_Patch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codeMatcher = new CodeMatcher(instructions);
            codeMatcher.MatchForward(false, new CodeMatch(OpCodes.Ldsfld, AccessTools.Field(typeof(DesignValues.KeypadCodes), nameof(DesignValues.KeypadCodes.ChurchToPosition1))))
                .ThrowIfInvalid("Failed to find DesignValues.KeypadCodes.ChurchToPosition1")
                .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Ldc_I4_0))
                .InsertAndAdvance(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ChurchElevatorControlTerminal_onInteract_Patch), nameof(GetElevatorCode))));
            codeMatcher.MatchForward(false, new CodeMatch(OpCodes.Ldsfld, AccessTools.Field(typeof(DesignValues.KeypadCodes), nameof(DesignValues.KeypadCodes.ChurchToPosition2))))
                .ThrowIfInvalid("Failed to find DesignValues.KeypadCodes.ChurchToPosition2")
                .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Ldc_I4_1))
                .InsertAndAdvance(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ChurchElevatorControlTerminal_onInteract_Patch), nameof(GetElevatorCode))));
            codeMatcher.MatchForward(false, new CodeMatch(OpCodes.Ldsfld, AccessTools.Field(typeof(DesignValues.KeypadCodes), nameof(DesignValues.KeypadCodes.ChurchToPosition3))))
                .ThrowIfInvalid("Failed to find DesignValues.KeypadCodes.ChurchToPosition3")
                .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Ldc_I4_2))
                .InsertAndAdvance(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ChurchElevatorControlTerminal_onInteract_Patch), nameof(GetElevatorCode))));
            codeMatcher.MatchForward(false, new CodeMatch(OpCodes.Ldsfld, AccessTools.Field(typeof(DesignValues.KeypadCodes), nameof(DesignValues.KeypadCodes.ChurchToPosition4))))
                .ThrowIfInvalid("Failed to find DesignValues.KeypadCodes.ChurchToPosition4")
                .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Ldc_I4_3))
                .InsertAndAdvance(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ChurchElevatorControlTerminal_onInteract_Patch), nameof(GetElevatorCode))));
            codeMatcher.MatchForward(false, new CodeMatch(OpCodes.Ldsfld, AccessTools.Field(typeof(DesignValues.KeypadCodes), nameof(DesignValues.KeypadCodes.ChurchSummon))))
                .ThrowIfInvalid("Failed to find DesignValues.KeypadCodes.ChurchSummon")
                .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ChurchElevatorControlTerminal_onInteract_Patch), nameof(GetSummonCode))));
            return codeMatcher.Instructions();
        }

        private static List<int> GetElevatorCode(int index)
        {
            if (Plugin.Randomizer.TryGetKeypadInfo(KeypadInstance.ChurchElevator, out var passcodes, out var buttons) && index < passcodes.Length)
            {
                return passcodes[index];
            }
            // Fall back to the vanilla codes.
            return index switch
            {
                0 => DesignValues.KeypadCodes.ChurchToPosition1,
                1 => DesignValues.KeypadCodes.ChurchToPosition2,
                3 => DesignValues.KeypadCodes.ChurchToPosition4,
                _ => DesignValues.KeypadCodes.ChurchToPosition3,
            };
        }

        private static List<int> GetSummonCode()
        {
            if (Plugin.Randomizer.TryGetKeypadInfo(KeypadInstance.ChurchElevatorSummon, out var passcodes, out var buttons) && passcodes.Length > 0)
            {
                return passcodes[0];
            }
            return DesignValues.KeypadCodes.ChurchSummon;
        }
    }

    /// <summary>
    /// Updates Tetsa's Lab frequency codes with codes from the Randomizer.
    /// </summary>
    [HarmonyPatch(typeof(Interactable))]
    [HarmonyPatch("Awake")]
    public static class Interactable_Awake_Patch
    {
        private static readonly AccessTools.FieldRef<RolodexInteractable, int[]> s_codeRef =
            AccessTools.FieldRefAccess<RolodexInteractable, int[]>("code");

        static void Postfix(Interactable __instance)
        {
            if (__instance is RolodexInteractable r)
            {
                var code = s_codeRef(r);
                if ((code.SequenceEqual([2, 7, 1, 8]) && Plugin.Randomizer.TryGetFrequencyCode(0, out int[] newCode)) ||
                    (code.SequenceEqual([5, 8, 4, 2]) && Plugin.Randomizer.TryGetFrequencyCode(1, out newCode)) ||
                    (code.SequenceEqual([9, 9, 1, 7]) && Plugin.Randomizer.TryGetFrequencyCode(2, out newCode)))
                {
                    s_codeRef(r) = newCode;
                }
            }
        }
    }

    /// <summary>
    /// Updates codes in game copy obtained via GetDisplayCopy.
    /// </summary>
    [HarmonyPatch(typeof(GameCopy))]
    [HarmonyPatch(nameof(GameCopy.GetDisplayCopy))]
    public static class GameCopy_GetDisplayCopy_Patch
    {
        static void Postfix(string _id, ref string __result)
        {
            UpdateGameCopy(_id, ref __result);
        }

        public static void UpdateGameCopy(string id, ref string copy)
        {
            if ((id == "RIFT_INTROELEVATORCODE_6" || id == "LEAD_LOOMELEVATORPASSCODE") &&
                Plugin.Randomizer.TryGetKeypadInfo(KeypadInstance.GlaiveElevator, out var passcodes, out var buttons))
            {
                copy = copy.Replace("1254", string.Join("", passcodes.First()));
            }
            else if ((id == "NOTE_WESTTOWER_HEADER" || id == "LEAD_LAKEWESTTOWERNOTE" || id == "LEAD_LAKETOWERSCONCLUSION" || id == "LEAD_UNDERLAKEELEVATORUPCODE") &&
                Plugin.Randomizer.TryGetKeypadInfo(KeypadInstance.UnderlakeElevator, out passcodes, out buttons))
            {
                var passcode = passcodes.First().ToArray();
                copy = copy.Replace("456.3", $"{string.Join("", passcode[..^1])}.{passcode.Last()}").Replace("4563", string.Join("", passcode));
            }
            else if ((id == "LEAD_VOSCOURTYARDKEY" || id == "VOS_LAKEFIXED_6" || id == "VOS_LAKEFIXED_9" || id == "VOS_LAKEFIXED_10") &&
                Plugin.Randomizer.TryGetKeypadInfo(KeypadInstance.TownVosLocker, out passcodes, out buttons))
            {
                copy = copy.Replace("1", string.Join("", passcodes.First()));
            }
            else if ((id == "LEAD_TETSAPOWERBEACON1" || id == "TERM_TETSABEACON1_BODY") &&
                Plugin.Randomizer.TryGetFrequencyCode(0, out int[] code))
            {
                copy = copy.Replace("2.718", $"{code[0]}.{string.Join("", code[1..])}");
            }
            else if ((id == "LEAD_TETSAPOWERBEACON2" || id == "TERM_TETSABEACON2_BODY") &&
                Plugin.Randomizer.TryGetFrequencyCode(1, out code))
            {
                copy = copy.Replace("5.842", $"{code[0]}.{string.Join("", code[1..])}");
            }
            else if ((id == "LEAD_TETSAPOWERBEACON3" || id == "TERM_TETSABEACON3_BODY") &&
                Plugin.Randomizer.TryGetFrequencyCode(2, out code))
            {
                copy = copy.Replace("9.917", $"{code[0]}.{string.Join("", code[1..])}");
            }
        }
    }

    /// <summary>
    /// Updates codes in game copy obtained via TryGetDisplayCopy.
    /// </summary>
    [HarmonyPatch(typeof(GameCopy))]
    [HarmonyPatch(nameof(GameCopy.TryGetDisplayCopy))]
    public static class GameCopy_TryGetDisplayCopy_Patch
    {
        static void Postfix(string _id, ref string _copy, bool __result)
        {
            if (__result)
            {
                GameCopy_GetDisplayCopy_Patch.UpdateGameCopy(_id, ref _copy);
            }
        }
    }
}
