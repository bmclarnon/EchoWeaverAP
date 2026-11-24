// SPDX-License-Identifier: BSD-3-Clause
// Copyright (c) 2026 The EchoWeaverAP Authors

using geode;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;

namespace EchoWeaverAP.Patches;

/// <summary>
/// Harmony patches for gating abilities on item progression.
/// </summary>
public class AbilitiesPatcher
{
    private static readonly string InitialBonfireName = "Initial Bonfire";

    /// <summary>
    /// Prevents GiveKeyItemTrigger from giving KeyItems managed via collectables.
    /// </summary>
    [HarmonyPatch(typeof(GiveKeyItemTrigger))]
    [HarmonyPatch("onPlayerEnter")]
    public static class GiveKeyItemTrigger_onPlayerEnter_Patch
    {
        private static readonly AccessTools.FieldRef<GiveKeyItemTrigger, KeyItem> s_itemToGiveRef =
            AccessTools.FieldRefAccess<GiveKeyItemTrigger, KeyItem>("itemToGive");

        static bool Prefix(GiveKeyItemTrigger __instance)
        {
            KeyItem keyItem = s_itemToGiveRef(__instance);
            if (CollectablePatcher.AllKeyItems.Contains(keyItem))
            {
                Plugin.Logger.LogDebug($"Preventing GiveKeyItemTrigger from giving {keyItem}");
                return false;
            }
            else
            {
                Plugin.Logger.LogDebug($"Allowing GiveKeyItemTrigger to give {keyItem}");
                return true;
            }
        }
    }

    /// <summary>
    /// Unlocks abilities based on acquired KeyItems.
    /// </summary>
    // Patching Awake ensures callbacks are registered after initialization is complete.
    [HarmonyPatch(typeof(GameManager))]
    [HarmonyPatch("Awake")]
    public static class GameManager_Awake_Patch
    {
        static void Postfix()
        {
            PlayerData.OnItemAdded += OnItemAdded;
            GameManager.OnPlayerBeginningToDropIn += OnPlayerBeginningToDropIn;
        }

        // Unlock abilities when the KeyItem is acquired.
        private static void OnItemAdded(KeyItem item, int newValue)
        {
            switch (item)
            {
                case KeyItem.GlaiveRecall:
                    GameManager.Glaive?.Locker?.UnlockGlaive(false);
                    GameManager.Player?.DropHoldable();
                    GameManager.Player?.GiveGlaiveToPlayer();
                    break;

                case KeyItem.Reach:
                    GameManager.Instance?.AcquireAbility(Ability.WallJump);
                    break;

                case KeyItem.DoubleJump:
                    GameManager.Instance?.AcquireAbility(Ability.DoubleJump);
                    break;

                case KeyItem.InPlaceRecall:
                    GameManager.Instance?.AcquireAbility(Ability.Glide);
                    break;

                case KeyItem.Grapple:
                    GameManager.Instance?.AcquireAbility(Ability.Grapple);
                    break;

                default:
                    break;
            }
        }

        // Unlock abilities after looping.
        private static void OnPlayerBeginningToDropIn()
        {
            if (PlayerData.HasItem(KeyItem.Reach))
            {
                GameManager.Instance?.AcquireAbility(Ability.WallJump);
            }
            if (PlayerData.HasItem(KeyItem.DoubleJump))
            {
                GameManager.Instance?.AcquireAbility(Ability.DoubleJump);
            }
            if (PlayerData.HasItem(KeyItem.InPlaceRecall))
            {
                GameManager.Instance?.AcquireAbility(Ability.Glide);
            }
            if (PlayerData.HasItem(KeyItem.Grapple))
            {
                GameManager.Instance?.AcquireAbility(Ability.Grapple);
            }
        }
    }

    /// <summary>
    /// Removes starting abilities that are managed via KeyItems.
    /// </summary>
    [HarmonyPatch(typeof(GameManager))]
    [HarmonyPatch("GiveStartingAbilities")]
    public static class GameManager_GiveStartingAbilities_Patch
    {
        static void Postfix(GameManager __instance)
        {
            __instance.RemoveAbility(Ability.WallJump);
            __instance.RemoveAbility(Ability.DoubleJump);
        }
    }

    /// <summary>
    /// Disallow glaive recall without KeyItems.GlaiveRecall.
    /// </summary>
    [HarmonyPatch(typeof(RecallGlaive))]
    [HarmonyPatch(nameof(RecallGlaive.CheckAndUpdate))]
    public static class RecallGlaive_CheckAndUpdate_Patch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codeMatcher = new CodeMatcher(instructions);
            codeMatcher.MatchForward(true,
                new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(RecallGlaive), "isRecallInputDown")),
                new CodeMatch(i => i.opcode == OpCodes.Brfalse || i.opcode == OpCodes.Brfalse_S, name: "branch"))
                .ThrowIfInvalid("Failed to find RecallGlaive.isRecallInputDown()")
                .Advance(1)
                .InsertAndAdvance(
                    new CodeInstruction(OpCodes.Ldc_I4_S, (sbyte)KeyItem.GlaiveRecall),
                    CodeInstruction.Call(typeof(PlayerData), nameof(PlayerData.HasItem)),
                    new CodeInstruction(OpCodes.Brfalse, codeMatcher.NamedMatch("branch").operand));
            return codeMatcher.Instructions();
        }
    }

    /// <summary>
    /// Creates a new, non-interactable bonfire to hold the glaive at the start of the game.
    /// </summary>
    [HarmonyPatch]
    public static class GlaiveLocker_waitForDataLoaded_Patch
    {
        private static readonly AccessTools.FieldRef<GlaiveLocker, bool> s_isFirstBonfireRef =
            AccessTools.FieldRefAccess<GlaiveLocker, bool>("isFirstBonfire");
        private static readonly AccessTools.FieldRef<SaveID, string> s_saveIdRef =
            AccessTools.FieldRefAccess<SaveID, string>("fullID");
        private static readonly string s_InitialBonfireSaveId = "/" + InitialBonfireName;

        static MethodBase TargetMethod()
        {
            return AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(GlaiveLocker), "waitForDataLoaded"));
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codeMatcher = new CodeMatcher(instructions);
            codeMatcher.MatchForward(false,
                new CodeMatch(OpCodes.Call, AccessTools.PropertyGetter(typeof(PlayerData), nameof(PlayerData.HasBonfire))),
                new CodeMatch(i => i.opcode == OpCodes.Brtrue || i.opcode == OpCodes.Brtrue_S))
                .ThrowIfInvalid("Failed to find !PlayerData.HasBonfire")
                .RemoveInstructions(2);
            codeMatcher.MatchForward(false,
                new CodeMatch(i => i.IsLdloc()),
                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(GlaiveLocker), "id")),
                new CodeMatch(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(SaveID), nameof(SaveID.ID))),
                new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(PlayerData), nameof(PlayerData.SetCurrentBonfire))))
                .ThrowIfInvalid("Failed to find PlayerData.SetCurrentBonfire(this.id.ID)")
                .Advance(1)
                .RemoveInstructions(2)
                .SetInstructionAndAdvance(CodeInstruction.Call(typeof(GlaiveLocker_waitForDataLoaded_Patch), nameof(CloneLocker)));
            return codeMatcher.Instructions();
        }

        private static void CloneLocker(GlaiveLocker locker)
        {
            var obj = UnityEngine.Object.Instantiate(locker.gameObject);
            obj.name = InitialBonfireName;
            s_isFirstBonfireRef(obj.GetComponent<GlaiveLocker>()) = false;
            s_saveIdRef(obj.GetComponent<SaveID>()) = s_InitialBonfireSaveId;
            // Put the bonfire in a known location (just out of bounds of the start room).
            obj.transform.position = new Vector3(60f, -236f, 0f);

            // Don't render the bonfire.
            foreach (var r in obj.GetComponentsInChildren<Renderer>())
            {
                r.enabled = false;
            }

            // Make the bonfire the default for a new game.
            if (!PlayerData.HasBonfire)
            {
                PlayerData.SetCurrentBonfire(s_InitialBonfireSaveId);
            }
        }
    }

    /// <summary>
    /// Prevents interacting with the initial locker.
    /// </summary>
    [HarmonyPatch(typeof(GlaiveLocker))]
    [HarmonyPatch("allowedToInteract", MethodType.Getter)]
    public static class GlaiveLocker_allowedToInteract_Patch
    {
        static void Postfix(GlaiveLocker __instance, ref bool __result)
        {
            if (__result && __instance.name == InitialBonfireName)
            {
                __result = false;
            }
        }
    }

    /// <summary>
    /// Prevent lights on the initial bonfire.
    /// </summary>
    [HarmonyPatch(typeof(GlaiveLocker))]
    [HarmonyPatch("setBonfireLights")]
    public static class GlaiveLocker_setBonfireLights_Patch
    {
        static void Prefix(GlaiveLocker __instance, ref bool _enable)
        {
            if (_enable && __instance.name == InitialBonfireName)
            {
                _enable = false;
            }
        }
    }

    /// <summary>
    /// Hides the glaive on the map if it's in the initial bonfire.
    /// </summary>
    [HarmonyPatch(typeof(MapUILocationGroup))]
    [HarmonyPatch(nameof(MapUILocationGroup.PopulateRoomInfo))]
    public static class MapUILocationGroup_PopulateRoomInfo_Patch
    {
        static void Postfix(ref RectTransform _glaiveMarker)
        {
            if (_glaiveMarker != null && GameManager.Glaive?.Locker?.name == InitialBonfireName)
            {
                _glaiveMarker.gameObject.GetComponent<Image>()?.SetAlpha(0f);
                _glaiveMarker = null;
            }
        }
    }

    /// <summary>
    /// Disables the recall impulse until the item has been acquired.
    /// </summary>
    [HarmonyPatch(typeof(BattleManager))]
    [HarmonyPatch(nameof(BattleManager.DoEnthWarpExplosion))]
    public static class BattleManager_DoEnthWarpExplosion_Patch
    {
        static bool Prefix()
        {
            return PlayerData.HasItem(KeyItem.RecallImpulse);
        }
    }

    /// <summary>
    /// Disables warping until KeyItem.GlaiveWarp or KeyItem.LinkingRoom has been acquired.
    /// 
    /// This isn't strictly necessary, but it makes it less likely that players hit the (currently) poor
    /// visuals when a warp fails.
    /// </summary>
    [HarmonyPatch(typeof(PlayerController))]
    [HarmonyPatch(nameof(PlayerController.PerformWarpToGlaive))]
    public static class PlayerController_PerformWarpToGlaive_Patch
    {
        private static readonly MethodInfo s_doFailedWarpAttempt = AccessTools.Method(typeof(PlayerController), "doFailedWarpAttempt");

        static bool Prefix(PlayerController __instance, bool __runOriginal)
        {
            if (!__runOriginal || (PlayerData.HasItem(KeyItem.GlaiveWarp) && GameManager.Glaive?.Locker?.name != InitialBonfireName) || PlayerData.HasItem(KeyItem.LinkingRoom))
            {
                return true;
            }

            IEnumerator doFailedWarpAttempt()
            {
                s_doFailedWarpAttempt.Invoke(__instance, []);
                yield break;
            }

            __instance.StartCoroutine(doFailedWarpAttempt());
            return false;
        }
    }

    /// <summary>
    /// Prevents awarding the glaive and KeyItems.GlaiveWarp during a glaive warp.
    /// </summary>
    [HarmonyPatch]
    public static class PlayerController_performWarpToGlaive_Patch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(PlayerController), "performWarpToGlaive"));
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codeMatcher = new CodeMatcher(instructions);

            // Don't give KeyItem.GlaiveWarp.
            codeMatcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldc_I4_S, (sbyte)KeyItem.GlaiveWarp),
                new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(GameManager), nameof(GameManager.GiveKeyItemToPlayer))))
                .ThrowIfInvalid("Failed to find GameManager.GiveKeyItemToPlayer(KeyItem.GlaiveWarp)")
                .RemoveInstructions(2);

            // Require KeyItem.GlaiveRecall for the glaive to be given to the player.
            codeMatcher.MatchForward(true,
                new CodeMatch(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(Glaive), nameof(Glaive.Locker))),
                new CodeMatch(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(GlaiveLocker), nameof(GlaiveLocker.IsIvo))),
                new CodeMatch(i => i.opcode == OpCodes.Br || i.opcode == OpCodes.Br_S),
                new CodeMatch(OpCodes.Ldc_I4_0),
                new CodeMatch(OpCodes.Stfld),
                new CodeMatch(OpCodes.Ldarg_0),
                new CodeMatch(OpCodes.Ldfld),
                new CodeMatch(i => i.opcode == OpCodes.Brtrue || i.opcode == OpCodes.Brtrue_S, name: "branch"))
                .ThrowIfInvalid("Failed to find !keepGlaiveWithIvo")
                .Advance(1)
                .Insert(
                    new CodeInstruction(OpCodes.Ldc_I4_S, (sbyte)KeyItem.GlaiveRecall),
                    CodeInstruction.Call(typeof(PlayerData), nameof(PlayerData.HasItem)),
                    new CodeInstruction(OpCodes.Brfalse, codeMatcher.NamedMatch("branch").operand));

            return codeMatcher.Instructions();
        }
    }

    /// <summary>
    /// Requires KeyItems.GlaiveWarp or KeyItems.LinkingRoom to warp, based on destination.
    /// </summary>
    [HarmonyPatch]
    public static class WarpEffectsManager_PanCameraForWarp_Patch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(WarpEffectsManager), nameof(WarpEffectsManager.PanCameraForWarp)));
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var codeMatcher = new CodeMatcher(instructions, generator);

            // Require KeyItems.LinkingRoom to set PlayerController.WarpingToLinkingRoom.
            codeMatcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldarg_0),
                new CodeMatch(OpCodes.Ldfld),
                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(PlayerController), nameof(PlayerController.WarpingToLinkingRoom))),
                new CodeMatch(i => i.opcode == OpCodes.Brtrue || i.opcode == OpCodes.Brtrue_S, name: "branch"),
                new CodeMatch(OpCodes.Call, AccessTools.PropertyGetter(typeof(EchoRift), nameof(EchoRift.CurrentMajorRift))),
                new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(UnityEngine.Object), "op_Implicit")),
                new CodeMatch(i => i.opcode == OpCodes.Brtrue || i.opcode == OpCodes.Brtrue_S),
                new CodeMatch(OpCodes.Ldsfld, AccessTools.Field(typeof(GameManager), nameof(GameManager.InFinalGauntlet))),
                new CodeMatch(i => i.opcode == OpCodes.Brtrue || i.opcode == OpCodes.Brtrue_S))
                .Repeat(matcher =>
                {
                    matcher.Advance(4)
                        .InsertAndAdvance(
                            new CodeInstruction(OpCodes.Ldc_I4_S, (sbyte)KeyItem.LinkingRoom),
                            CodeInstruction.Call(typeof(PlayerData), nameof(PlayerData.HasItem)),
                            new CodeInstruction(OpCodes.Brfalse, matcher.NamedMatch("branch").operand))
                        .Advance(5);
                },
                err => Plugin.Logger.LogError("No matches for !player.WarpingToLinkingRoom && !EchoRift.CurrentMajorRift && !GameManager.InFinalGauntlet"));

            // Require WarpingToLinkingRoom or KeyItems.GlaiveWarp to update the player position during a warp.
            var skip = generator.DefineLabel();
            codeMatcher.Start()
                .MatchForward(false,
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldfld, name: "loadPlayer"),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(PlayerController), nameof(PlayerController.Rigidbody))),
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldfld),
                    new CodeMatch(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(Component), nameof(Component.transform))),
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldfld),
                    new CodeMatch(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(PlayerController.WarpUpdateInfo), nameof(PlayerController.WarpUpdateInfo.FinalPosition))),
                    new CodeMatch(OpCodes.Call),
                    new CodeMatch(OpCodes.Dup),
                    new CodeMatch(OpCodes.Stloc_S),
                    new CodeMatch(OpCodes.Callvirt, AccessTools.PropertySetter(typeof(Transform), nameof(Transform.position))),
                    new CodeMatch(OpCodes.Ldloc_S),
                    new CodeMatch(OpCodes.Call),
                    new CodeMatch(OpCodes.Callvirt, AccessTools.PropertySetter(typeof(Rigidbody2D), nameof(Rigidbody2D.position))),
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldfld, name: "loadInLinkingRoom"))
                .ThrowIfInvalid("Failed to find player.Rigidbody = (player.transform.position = _info.FinalPosition)")
                .Advance(1)
                .InsertAndAdvance(
                    codeMatcher.NamedMatch("loadInLinkingRoom").Clone(),
                    new CodeInstruction(OpCodes.Ldarg_0),
                    codeMatcher.NamedMatch("loadPlayer").Clone(),
                    CodeInstruction.Call(typeof(WarpEffectsManager_PanCameraForWarp_Patch), nameof(CanWarp)),
                    new CodeInstruction(OpCodes.Brfalse, skip),
                    new CodeInstruction(OpCodes.Ldarg_0))
                .Advance(15)
                .AddLabels([skip]);
            return codeMatcher.Instructions();
        }

        private static bool CanWarp(bool loadInLinkingRoom, PlayerController player)
        {
            return loadInLinkingRoom || player.WarpingToLinkingRoom || (PlayerData.HasItem(KeyItem.GlaiveWarp) && GameManager.Glaive?.Locker?.name != InitialBonfireName);
        }
    }

    /// <summary>
    /// Prevents touching the glaive from granting Grapple.
    /// </summary>
    [HarmonyPatch(typeof(GameManager))]
    [HarmonyPatch(nameof(GameManager.GlaiveTouchedFirstTimeThisLoop))]
    public static class GameManager_GlaiveTouchedFirstTimeThisLoop_Patch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codeMatcher = new CodeMatcher(instructions);
            codeMatcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldarg_0),
                new CodeMatch(OpCodes.Ldc_I4_5),
                new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(GameManager), nameof(GameManager.AcquireAbility))))
                .ThrowIfInvalid("Failed to find GameManager.AcquireAbility(Ability.Grapple)")
                .RemoveInstructions(3);
            return codeMatcher.Instructions();
        }
    }

    /// <summary>
    /// Prevents weaver overclock without KeyItem.WeaverOverclock.
    /// </summary>
    [HarmonyPatch]
    public static class TimeExtenderInteractable_flowFromOneTargetToAnother_Patch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(TimeExtenderInteractable), "flowFromOneTargetToAnother"));
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codeMatcher = new CodeMatcher(instructions);
            codeMatcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldc_I4_1),
                new CodeMatch(OpCodes.Stsfld, AccessTools.Field(typeof(GameManager), nameof(GameManager.PlayerCanOverclock))))
                .ThrowIfInvalid("Failed to find GameManager.PlayerCanOverclock = true")
                .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Ldc_I4_S, (sbyte)KeyItem.WeaverOverclock))
                .InsertAndAdvance(CodeInstruction.Call(typeof(PlayerData), nameof(PlayerData.HasItem)));
            return codeMatcher.Instructions();
        }
    }

    /// <summary>
    /// Prevent Twin Sight when diving into the glaive without the KeyItem.
    /// </summary>
    [HarmonyPatch(typeof(TwinSight))]
    [HarmonyPatch(nameof(TwinSight.OnDiveImpact))]
    public static class TwinSight_OnDiveImpact_Patch
    {
        static bool Prefix()
        {
            return PlayerData.HasItem(KeyItem.GlaiveInfusion);
        }
    }

    /// <summary>
    /// Require KeyItem.MendTime to break time.
    /// </summary>
    [HarmonyPatch(typeof(PlayerController))]
    [HarmonyPatch("checkForManuallyEndLoop")]
    public static class PlayerController_checkForManuallyEndLoop_Patch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codeMatcher = new CodeMatcher(instructions);
            codeMatcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldarg_0),
                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(PlayerController), "weaverFlowerHeldWhenInteractDownThisFrame")),
                new CodeMatch(i => i.opcode == OpCodes.Brfalse || i.opcode == OpCodes.Brfalse_S, name: "branch"),
                new CodeMatch(OpCodes.Ldarg_0),
                new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(PlayerController), nameof(PlayerController.GetCurrentlyHeldItem))),
                new CodeMatch(OpCodes.Ldc_I4_S, (sbyte)Item.WeaverFlower))
                .ThrowIfInvalid("Failed to find this.weaverFlowerHeldWhenInteractDownThisFrame && this.GetCurrentlyHeldItem() == Item.WeaverFlower")
                .Advance(3)
                .InsertAndAdvance(
                    new CodeInstruction(OpCodes.Ldc_I4_S, (sbyte)KeyItem.MendTime),
                    CodeInstruction.Call(typeof(PlayerData), nameof(PlayerData.HasItem)),
                    new CodeInstruction(OpCodes.Brfalse, codeMatcher.NamedMatch("branch").operand));
            return codeMatcher.Instructions();
        }
    }

    /// <summary>
    /// Prevents Unbinding from granting amplification or twin sight if not yet unlocked.
    /// </summary>
    [HarmonyPatch]
    public static class GameManager_staggerUnlockAllPowersForUnbinding_Patch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(GameManager), "staggerUnlockAllPowersForUnbinding"));
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codeMatcher = new CodeMatcher(instructions);
            // Prevent calling GameManager.Instance.GlaiveTouchedFirstTimeThisLoop.
            codeMatcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldsfld, AccessTools.Field(typeof(GameManager), nameof(GameManager.PlayerHasTouchedGlaiveOnce))),
                new CodeMatch(i => i.opcode == OpCodes.Brtrue || i.opcode == OpCodes.Brtrue_S))
                .ThrowIfInvalid("Failed to find !GameManager.PlayerHasTouchedGlaiveOnce")
                .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Ldc_I4_1));
            // Require KeyItem.GlaiveInfusion to call GameManager.SetGlaiveEnthInfused.
            codeMatcher.MatchForward(true,
                new CodeMatch(OpCodes.Call, AccessTools.PropertyGetter(typeof(GameManager), nameof(GameManager.GlaiveEnthPowered))),
                new CodeMatch(i => i.opcode == OpCodes.Brtrue || i.opcode == OpCodes.Brtrue_S, name: "branch"))
                .ThrowIfInvalid("Failed to find !GameManager.GlaiveEnthPowered")
                .Advance(1)
                .InsertAndAdvance(
                    new CodeInstruction(OpCodes.Ldc_I4_S, (sbyte)KeyItem.GlaiveInfusion),
                    CodeInstruction.Call(typeof(PlayerData), nameof(PlayerData.HasItem)),
                    new CodeInstruction(OpCodes.Brfalse, codeMatcher.NamedMatch("branch").operand));
            // Require KeyItem.WeaverOverclock to call GameManager.SetPlayerOverclocked.
            codeMatcher.MatchForward(true,
                new CodeMatch(OpCodes.Call, AccessTools.PropertyGetter(typeof(GameManager), nameof(GameManager.PlayerOverclocked))),
                new CodeMatch(i => i.opcode == OpCodes.Brtrue || i.opcode == OpCodes.Brtrue_S, name: "branch"))
                .ThrowIfInvalid("Failed to find !GameManager.PlayerOverclocked")
                .Advance(1)
                .InsertAndAdvance(
                    new CodeInstruction(OpCodes.Ldc_I4_S, (sbyte)KeyItem.WeaverOverclock),
                    CodeInstruction.Call(typeof(PlayerData), nameof(PlayerData.HasItem)),
                    new CodeInstruction(OpCodes.Brfalse, codeMatcher.NamedMatch("branch").operand));
            return codeMatcher.Instructions();
        }
    }

    /// <summary>
    /// Makes jumping off a wall count as a second jump without the WallJump ability.
    /// </summary>
    [HarmonyPatch(typeof(PlayerController))]
    [HarmonyPatch("performWallJump")]
    public static class PlayerController_performWallJump_Patch
    {
        private static readonly AccessTools.FieldRef<PlayerController, int> s_numJumpsRef =
            AccessTools.FieldRefAccess<PlayerController, int>("numJumps");

        static void Postfix(PlayerController __instance, bool __result)
        {
            if (__result && !GameManager.Instance.HasAbility(Ability.WallJump))
            {
                s_numJumpsRef(__instance) -= 1;
            }
        }
    }

    /// <summary>
    /// Prevent giving KeyItem.MazeLang for completing high-clearance keypads.
    /// </summary>
    [HarmonyPatch]
    public static class KeypadInjector_initialize_Patch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(KeypadInjector)))
            {
                if (method.Name.Contains("<initialize>b__"))
                {
                    yield return method;
                }
            }
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codeMatcher = new CodeMatcher(instructions);
            codeMatcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldc_I4, (int)KeyItem.MazeLang),
                new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(GameManager), nameof(GameManager.GiveKeyItemToPlayer))))
                .Repeat(matcher => matcher.RemoveInstructions(2));
            return codeMatcher.Instructions();
        }
    }

    /// <summary>
    /// Hides keypad icons without KeyItems.MazeLang.
    /// </summary>
    [HarmonyPatch(typeof(KeypadOverlay))]
    [HarmonyPatch("Awake")]
    public static class KeypadOverlay_Awake_Patch
    {
        private static readonly AccessTools.FieldRef<KeypadOverlay, GameObject> s_churchControlsGroupRef =
            AccessTools.FieldRefAccess<KeypadOverlay, GameObject>("churchControlsGroup");
        private static readonly AccessTools.FieldRef<KeypadOverlay, GameObject[]> s_instanceSymbolGroupsRef =
            AccessTools.FieldRefAccess<KeypadOverlay, GameObject[]>("instanceSymbolGroups");
        private static readonly AccessTools.FieldRef<KeypadOverlay, GameObject[]> s_bigInstanceSymbolGroupsRef =
            AccessTools.FieldRefAccess<KeypadOverlay, GameObject[]>("bigInstanceSymbolGroups");

        static void Postfix(KeypadOverlay __instance)
        {
            bool hasMazeLang = PlayerData.HasItem(KeyItem.MazeLang);
            GameObject churchControlsGroup = s_churchControlsGroupRef(__instance);
            if (churchControlsGroup.activeSelf)
            {
                // We don't want to disable the whole controls group because it contains more
                // than just the digit images. Instead, we disable the specific sub-components,
                // which also requires re-enabling them when MazeLang is acquired.
                foreach (Image image in churchControlsGroup.GetComponentsInChildren<Image>())
                {
                    if (image.sprite.GetName().StartsWith("Digit_"))
                    {
                        image.gameObject.SetActive(hasMazeLang);
                    }
                }
            }
            else if (!hasMazeLang)
            {
                foreach (GameObject symbolGroup in s_instanceSymbolGroupsRef(__instance))
                {
                    symbolGroup.SetActive(false);
                }
                foreach (GameObject symbolGroup in s_bigInstanceSymbolGroupsRef(__instance))
                {
                    symbolGroup.SetActive(false);
                }
            }
        }
    }
}
