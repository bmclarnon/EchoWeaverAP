// SPDX-License-Identifier: BSD-3-Clause
// Copyright (c) 2026 The EchoWeaverAP Authors

using EchoWeaverAP.Randomizer;
using HarmonyLib;

namespace EchoWeaverAP.Patches;

/// <summary>
/// Harmony patches to record when game goals are completed.
/// </summary>
public class GoalPatcher
{
    [HarmonyPatch(typeof(GameManager))]
    [HarmonyPatch("KickOffEndOfGame")]
    public static class GameManager_KickOffEndOfGame_Patch
    {
        static void Postfix()
        {
            Plugin.Randomizer.SetGoalAchieved(Goal.BreakTheCycle);
            if (RunDataKeys.DidAllNPCQuestsThisLoop())
            {
                Plugin.Randomizer.SetGoalAchieved(Goal.BetterWithFriends);
            }
        }
    }
}
