// SPDX-License-Identifier: BSD-3-Clause
// Copyright (c) 2026 The EchoWeaverAP Authors

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using BepInEx;
using Newtonsoft.Json;
using UnityEngine;

namespace EchoWeaverAP.Randomizer;

public class ArchipelagoRandomizer : IRandomizer
{
    public bool Initialized { get { return _gameData != null; } }
    public event Action OnInitialized;
    public event Action<(long ItemId, long? LocationId)> OnItemReceived;
    public event Action<long> OnLocationCheckedRemotely;
    public event Action<string> OnConnectionStatusChange;

    public bool Connected { get { return _session?.Socket.Connected == true; } }

    private ArchipelagoSession _session = null;
    private GameData _gameData = null;
    private string _server = null;
    private string _user = null;
    private string _password = null;
    private bool _attemptingConnection = false;

    private bool _saveInProgress = false;
    private bool _concurrentSaveRequested = false;

    private string SavePath { get { return Path.Combine(Application.persistentDataPath, MyPluginInfo.PLUGIN_NAME, "archipelago.json"); } }

    public ArchipelagoRandomizer()
    {
        ThreadingHelper.Instance.StartAsyncInvoke(() =>
        {
            GameData gameData = null;
            string savePath = SavePath;
            if (File.Exists(savePath))
            {
                try
                {
                    gameData = JsonConvert.DeserializeObject<GameData>(File.ReadAllText(savePath));
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogError($"Loading failed: {e}");
                }
            }
            return () =>
            {
                if (_gameData == null && gameData != null)
                {
                    _gameData = gameData;
                    OnInitialized?.Invoke();
                }
            };
        });
    }

    public void Connect(string server, string user, string password = null)
    {
        // Prevent concurrent attempts.
        if (_attemptingConnection) return;
        _attemptingConnection = true;

        Plugin.Logger.LogInfo($"Connecting to {server} as {user}");

        if (Connected)
        {
            Disconnect();
        }

        _server = server;
        _user = user;
        _password = password;
        bool initialized = Initialized;
        ThreadingHelper.Instance.StartAsyncInvoke(() =>
        {
            var session = ArchipelagoSessionFactory.CreateSession(server);

            LoginResult result;
            try
            {
                result = session.TryConnectAndLogin(
                    "Echo Weaver",
                    user,
                    initialized ? ItemsHandlingFlags.RemoteItems : ItemsHandlingFlags.IncludeStartingInventory,
                    version: new Version(0, 6, 8),
                    tags: ["NoText"],
                    password: password,
                    requestSlotData: !initialized);
            }
            catch (Exception e)
            {
                result = new LoginFailure(e.GetBaseException().Message);
            }

            if (result.Successful)
            {
                var success = (LoginSuccessful)result;
                session.Socket.ErrorReceived += OnErrorReceived;
                session.Socket.SocketClosed += OnSocketClosed;

                GameData gameData = null;
                if (!initialized)
                {
                    var slotData = success.SlotData;

                    var scoutedLocations = session.Locations.ScoutLocationsAsync([.. session.Locations.AllLocations]).GetAwaiter().GetResult().ToDictionary(e => e.Key, e =>
                    {
                        var itemId = e.Value.Player == session.Players.ActivePlayer ? e.Value.ItemId : -1;
                        var importance = e.Value.Flags.HasFlag(ItemFlags.Advancement) ? ItemImportance.Progression : e.Value.Flags.HasFlag(ItemFlags.NeverExclude) ? ItemImportance.Useful : e.Value.Flags.HasFlag(ItemFlags.Trap) ? ItemImportance.Trap : ItemImportance.Filler;
                        var description = itemId == -1 ? $"{e.Value.ItemDisplayName} from {session.Players.GetPlayerAlias(e.Value.Player)}'s world" : null;
                        return (itemId, importance, description);
                    });
                    gameData = new GameData(session.RoomState.Seed, session.ConnectionInfo.Slot, success.SlotData, scoutedLocations);
                }

                return () =>
                {
                    _attemptingConnection = false;
                    if (_gameData == null && gameData != null)
                    {
                        _gameData = gameData;
                        OnInitialized?.Invoke();
                    }
                    else if (_gameData.RoomSeed != session.RoomState.Seed || _gameData.Slot != session.ConnectionInfo.Slot)
                    {
                        OnConnectionStatusChange?.Invoke("Remote game does not match local save data; consider resetting local data.");
                        session.Socket.ErrorReceived -= OnErrorReceived;
                        session.Socket.SocketClosed -= OnSocketClosed;
                        session.Socket.DisconnectAsync();
                        return;
                    }
                    _session = session;
                    _session.Items.ItemReceived += OnArchipelagoItemReceived;
                    _session.Locations.CheckedLocationsUpdated += OnCheckedLocationsUpdated;
                    OnConnectionStatusChange?.Invoke("Connected");
                    SaveAsync();

                    // Inform the server of any locations that were checked since the last sync.
                    var newCheckedLocations = _gameData.CheckedLocations.Except(session.Locations.AllLocationsChecked).ToArray();
                    if (newCheckedLocations.Length > 0)
                    {
                        _session.Locations.CompleteLocationChecksAsync(newCheckedLocations);
                    }

                    // Inform the server if the goal was achieved. There isn't an easy way to tell if this message was already sent,
                    // but duplicates should be rare.
                    if (_gameData.GoalAchieved == true)
                    {
                        _session.SetGoalAchieved();
                    }
                };
            }
            else
            {
                var failure = (LoginFailure)result;
                var shortMessage = failure.Errors.FirstOrDefault() ?? "Unknown Error";
                var fullMessage = $"Failed to connect to {server} as {user}:";
                foreach (string error in failure.Errors)
                {
                    fullMessage += $"\n    {error}";
                }
                foreach (ConnectionRefusedError error in failure.ErrorCodes)
                {
                    fullMessage += $"\n    {error}";
                }
                return () =>
                {
                    Plugin.Logger.LogWarning(fullMessage);
                    OnConnectionStatusChange?.Invoke(shortMessage);
                    _attemptingConnection = false;
                };
            }
        });
    }

    public void Disconnect()
    {
        _session?.Socket.DisconnectAsync();
        _session?.Items.ItemReceived -= OnArchipelagoItemReceived;
        _session?.Locations.CheckedLocationsUpdated -= OnCheckedLocationsUpdated;
        _session?.Socket.ErrorReceived -= OnErrorReceived;
        _session?.Socket.SocketClosed -= OnSocketClosed;
        _session = null;
        OnConnectionStatusChange?.Invoke("Disconnected");
    }

    public void MarkLocationChecked(long locationId)
    {
        if (_gameData.CheckedLocations.Add(locationId))
        {
            Plugin.Logger.LogDebug($"Location {locationId} has been checked");
            SaveAsync();
            if (Connected)
            {
                _session.Locations.CompleteLocationChecksAsync([locationId]);
            }
        }
    }

    public bool TryPeekLocation(long locationId, out long itemId, out ItemImportance importance, out string description)
    {
        if (_gameData?.ScoutedLocations.TryGetValue(locationId, out var result) == true)
        {
            itemId = result.ItemId;
            importance = result.Importance;
            description = result.Description;
            return true;
        }
        else
        {
            itemId = default;
            importance = default;
            description = default;
            return false;
        }
    }

    public bool TryGetKeypadInfo(KeypadInstance instance, out List<int>[] passcodes, out (Digit Digit, D8Symmetry Symmetry)[] buttons)
    {
        if (_gameData?.Keypads.TryGetValue(instance, out var result) == true)
        {
            passcodes = result.Passcodes;
            buttons = result.Buttons;
            return true;
        }
        else
        {
            passcodes = default;
            buttons = default;
            return false;
        }
    }

    public bool TryGetFrequencyCode(int index, out int[] code)
    {
        if (index < _gameData?.Frequencies.Length)
        {
            code = _gameData.Frequencies[index];
            return true;
        }
        else
        {
            code = default;
            return false;
        }
    }

    public void SetGoalAchieved(Goal goal)
    {
        if (goal == _gameData.Goal)
        {
            _gameData.GoalAchieved = true;
            _session?.SetGoalAchieved();
        }
    }

    public void Reset()
    {
        Plugin.Logger.LogInfo("Resetting cached Archipelago data");
        Disconnect();
        _gameData = null;
        SaveAsync();
    }

    private void OnArchipelagoItemReceived(ReceivedItemsHelper items)
    {
        ThreadingHelper.Instance.StartSyncInvoke(() =>
        {
            bool saveNeeded = false;
            while (_gameData.LastProcessedItemIndex < _session?.Items.Index)
            {
                var item = _session.Items.AllItemsReceived[_gameData.LastProcessedItemIndex];
                OnItemReceived?.Invoke((item.ItemId, item.Player == _session?.Players.ActivePlayer ? item.LocationId : null));
                ++_gameData.LastProcessedItemIndex;
                saveNeeded = true;
            }
            if (saveNeeded)
            {
                SaveAsync();
            }
        });
    }

    private void OnCheckedLocationsUpdated(ReadOnlyCollection<long> newCheckedLocations)
    {
        ThreadingHelper.Instance.StartSyncInvoke(() =>
        {
            bool saveNeeded = false;
            foreach (long locationId in newCheckedLocations)
            {
                if (_gameData?.CheckedLocations.Add(locationId) == true)
                {
                    saveNeeded = true;
                    OnLocationCheckedRemotely?.Invoke(locationId);
                }
            }
            if (saveNeeded)
            {
                SaveAsync();
                GameManager.SavePlayerData(true);
            }
        });
    }

    private void OnErrorReceived(Exception e, string message)
    {
        ThreadingHelper.Instance.StartSyncInvoke(() =>
        {
            Plugin.Logger.LogWarning(message);
            OnConnectionStatusChange?.Invoke(message);
        });
    }

    private void OnSocketClosed(string reason)
    {
        ThreadingHelper.Instance.StartSyncInvoke(() =>
        {
            Plugin.Logger.LogWarning(reason);
            OnConnectionStatusChange?.Invoke(reason);
            // TODO: Attempt to reconnect with randomized exponential backoff. The retry attempt should reset on successful connection.
            // Retries should be abandoned if the user tries to connect manually.
        });
    }

    private void SaveAsync()
    {
        if (_saveInProgress)
        {
            _concurrentSaveRequested = true;
            return;
        }

        _saveInProgress = true;
        string json = _gameData != null ? JsonConvert.SerializeObject(_gameData) : null;
        ThreadingHelper.Instance.StartAsyncInvoke(() =>
        {
            string savePath = SavePath;
            try
            {
                if (json == null)
                {
                    if (File.Exists(savePath))
                    {
                        File.Delete(savePath);
                    }
                }
                else
                {
                    string saveDir = Path.GetDirectoryName(savePath);
                    string tempPath = Path.Combine(saveDir, Path.GetRandomFileName());
                    Directory.CreateDirectory(saveDir);
                    File.WriteAllText(tempPath, json);
                    if (File.Exists(savePath))
                    {
                        File.Replace(tempPath, savePath, null);
                    }
                    else
                    {
                        File.Move(tempPath, savePath);
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError($"Saving failed: {e}");
            }

            return () =>
            {
                _saveInProgress = false;
                // If state was modified and another save was requested, save again immediately.
                if (_concurrentSaveRequested)
                {
                    _concurrentSaveRequested = false;
                    SaveAsync();
                }
            };
        });
    }
}
