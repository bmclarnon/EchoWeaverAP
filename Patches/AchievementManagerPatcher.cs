// SPDX-License-Identifier: BSD-3-Clause
// Copyright (c) 2026 The EchoWeaverAP Authors

using HarmonyLib;

namespace EchoWeaverAP.Patches;

/// <summary>
/// Harmony patches to prevent Achievements from unlocking when playing the randomizer.
/// </summary>
public class AchievementManagerPatcher
{
    [HarmonyPatch(typeof(geode.AchievementManager))]
    [HarmonyPatch("AcquireAchievement")]
    public static class AchievementManager_AcquireAchievement_Patch
    {
        static bool Prefix()
        {
            return false;
        }
    }
}
