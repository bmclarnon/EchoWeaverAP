// SPDX-License-Identifier: BSD-3-Clause
// Copyright (c) 2026 The EchoWeaverAP Authors

using System;
using HarmonyLib;
using UnityEngine;

namespace EchoWeaverAP.Patches;

/// <summary>
/// Harmony patches to disable minor rifts.
/// </summary>
public class RiftPatcher
{
    /// <summary>
    /// Hides non-major rifts.
    /// </summary>
    [HarmonyPatch(typeof(EchoRift))]
    [HarmonyPatch("Start")]
    public static class EchoRift_Start_Patch
    {
        private const string VivaryRift = "/The Cradle/Readables/Echo Rift/b252f448";

        private static readonly AccessTools.FieldRef<EchoRift, bool> s_mendedRef =
            AccessTools.FieldRefAccess<EchoRift, bool>("mended");
        private static readonly AccessTools.FieldRef<EchoRift, ParticleSystem> s_mainPfxRef =
            AccessTools.FieldRefAccess<EchoRift, ParticleSystem>("mainPfx");
        private static readonly AccessTools.FieldRef<EchoRift, ParticleSystem> s_seenPfxRef =
            AccessTools.FieldRefAccess<EchoRift, ParticleSystem>("seenPfx");

        static void Postfix(EchoRift __instance)
        {
            if (!__instance.IsMajorRift && __instance.GetComponent<SaveID>()?.ID != VivaryRift)
            {
                s_mendedRef(__instance) = true;
                s_mainPfxRef(__instance)?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                s_seenPfxRef(__instance)?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }

    /// <summary>
    /// Only allow major rifts to be mended.
    /// </summary>
    [HarmonyPatch(typeof(EchoRift))]
    [HarmonyPatch(nameof(EchoRift.CanAbsorb), MethodType.Getter)]
    public static class EchoRift_CanAbsorb_Patch
    {
        static void Postfix(EchoRift __instance, ref bool __result)
        {
            if (__result && __instance.IsMajorRift && !GameManager.InBrokenTime)
            {
                __result = false;
            }
        }
    }

    /// <summary>
    /// Converts the Monitoring and Observation rift into a readable console.
    /// </summary>
    [HarmonyPatch(typeof(EchoRift))]
    [HarmonyPatch("Start")]
    public static class EchoRift_Start_Patch2
    {
        private const string MonitoringAndObservationRift = "/Monitoring & Observation/Readables/Echo Rift - Keycode/822531c5";
        private const string MonitoringAndObservationConsole = "console image - normal";
        private const string StartingRoomConsole = "/Vault Entrance(Clone)/Widgets/Readable - Console (Closing Door)";

        private static readonly AccessTools.FieldRef<Readable, string> s_copyIdPrefixRef =
            AccessTools.FieldRefAccess<Readable, string>("copyIDPrefix");
        private static readonly AccessTools.FieldRef<Readable, bool> s_doOnCloseActionsRef =
            AccessTools.FieldRefAccess<Readable, bool>("doOnCloseActions");
        private static readonly AccessTools.FieldRef<Interactable, Lead> s_leadRef =
            AccessTools.FieldRefAccess<Interactable, Lead>("lead");
        private static readonly AccessTools.FieldRef<Interactable, bool> s_addMoreThanOneLeadRef =
            AccessTools.FieldRefAccess<Interactable, bool>("addMoreThenOneLead");

        static void Postfix(EchoRift __instance)
        {
            if (__instance.GetComponent<SaveID>()?.ID != MonitoringAndObservationRift) return;

            var originalConsole = __instance.transform.Find(MonitoringAndObservationConsole) ?? throw new InvalidOperationException($"Failed to find {MonitoringAndObservationConsole}");
            if (originalConsole.transform.localScale == Vector3.zero) return;
            originalConsole.transform.localScale = Vector3.zero;

            var console = GameObject.Instantiate(GameObject.Find(StartingRoomConsole) ?? throw new InvalidOperationException($"Failed to find {StartingRoomConsole}"), __instance.transform.parent);
            console.transform.position = originalConsole.position;
            console.transform.Find("flip-parent").localScale = new Vector3(1f, 1f, -1f);

            Readable readable = console.GetComponent<Readable>();
            s_copyIdPrefixRef(readable) = "RIFT_INTROELEVATORCODE_MERGED";
            s_doOnCloseActionsRef(readable) = false;
            s_leadRef(readable) = Lead.LoomElevatorPasscode;
            s_addMoreThanOneLeadRef(readable) = false;
        }
    }

    /// <summary>
    /// Provides game copy for the new Monitoring and Observation console.
    /// </summary>
    [HarmonyPatch(typeof(GameCopy))]
    [HarmonyPatch(nameof(GameCopy.GetDisplayCopy))]
    public static class GameCopy_GetDisplayCopy_Patch
    {
        static void Postfix(string _id, ref string __result)
        {
            if (_id == "RIFT_INTROELEVATORCODE_MERGED_HEADER")
            {
                __result = CleanCopy("RIFT_INTROELEVATORCODE_3");
            }
            else if (_id == "RIFT_INTROELEVATORCODE_MERGED_BODY")
            {
                __result = $"{CleanCopy("RIFT_INTROELEVATORCODE_4")} {CleanCopy("RIFT_INTROELEVATORCODE_5")}\n\n{CleanCopy("RIFT_INTROELEVATORCODE_6")}";
            }
        }

        private static string CleanCopy(string id)
        {
            string copy = GameCopy.GetDisplayCopy(id);
            if (copy.StartsWith("<wave>\""))
            {
                copy = copy[7..];
            }
            if (copy.EndsWith(":\""))
            {
                copy = copy[..^2];
            }
            else if (copy.EndsWith("\""))
            {
                copy = copy[..^1];
            }
            return copy;
        }
    }
}
