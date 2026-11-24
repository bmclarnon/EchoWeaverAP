// SPDX-License-Identifier: BSD-3-Clause
// Copyright (c) 2026 The EchoWeaverAP Authors

using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EchoWeaverAP.Randomizer;
using HarmonyLib;
using UnityEngine;
using UnityEngine.CrashReportHandler;
using UnityEngine.InputSystem;

namespace EchoWeaverAP;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInProcess("Echo Weaver.exe")]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    internal static IRandomizer Randomizer { get; } = new ArchipelagoRandomizer();

    private ConfigEntry<string> _serverAddress;
    private ConfigEntry<string> _user;
    private ConfigEntry<string> _password;
    private string _statusMessage = "Disconnected";
    private bool _showWindow = Randomizer is ArchipelagoRandomizer;

    private void Awake()
    {
        Logger = base.Logger;
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

        CrashReportHandler.enableCaptureExceptions = false;
        CrashReportHandler.logBufferSize = 0;
        Logger.LogInfo("Unity Cloud Diagnostics exception capture disabled");

        if (Randomizer is ArchipelagoRandomizer randomizer)
        {
            _serverAddress = Config.Bind("Archipelago", "ServerAddress", "archipelago.gg:38281", "The address of the Archipelago server");
            _user = Config.Bind("Archipelago", "SlotName", "Player", "The player name of the game slot");
            _password = Config.Bind("Archipelago", "Password", "", "The Archipelago server password");
            randomizer.OnConnectionStatusChange += OnConnectionStatusChange;
        }
    }

    private void OnDestroy()
    {
        if (Randomizer is ArchipelagoRandomizer randomizer)
        {
            randomizer.OnConnectionStatusChange -= OnConnectionStatusChange;
        }
    }

    private void Start()
    {
        ApplyHarmonyPatches();
    }

    private void Update()
    {
        if (Keyboard.current.f8Key.wasPressedThisFrame && Randomizer is ArchipelagoRandomizer)
        {
            _showWindow ^= true;
        }
    }

    private void OnGUI()
    {
        if (_showWindow && Randomizer is ArchipelagoRandomizer randomizer)
        {
            GUI.Box(new Rect(10, 10, 300, 190), "Archipelago Connection (F8 to Hide)");

            GUI.Label(new Rect(20, 40, 80, 20), "Server:");
            _serverAddress.Value = GUI.TextField(new Rect(100, 40, 200, 20), _serverAddress.Value);

            GUI.Label(new Rect(20, 65, 80, 20), "Slot Name:");
            _user.Value = GUI.TextField(new Rect(100, 65, 200, 20), _user.Value);

            GUI.Label(new Rect(20, 90, 80, 20), "Password:");
            _password.Value = GUI.PasswordField(new Rect(100, 90, 200, 20), _password.Value, '●');

            if (!randomizer.Connected)
            {
                if (GUI.Button(new Rect(190, 125, 110, 25), "Connect"))
                {
                    randomizer.Connect(_serverAddress.Value, _user.Value, _password.Value);
                    _statusMessage = "Connecting...";
                }
            }
            else
            {
                if (GUI.Button(new Rect(190, 125, 110, 25), "Disconnect"))
                {
                    randomizer.Disconnect();
                    _statusMessage = "Disconnected";
                }
            }

            GUI.Label(new Rect(20, 160, 280, 40), $"Status: {_statusMessage}");
        }
    }

    private void ApplyHarmonyPatches()
    {
        Logger.LogInfo("Applying Harmony patches...");
        Harmony harmony = new(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll();
        Logger.LogInfo("Harmony patches applied.");
    }

    private void OnConnectionStatusChange(string message)
    {
        _statusMessage = message;
    }
}
