// SPDX-License-Identifier: BSD-3-Clause
// Copyright (c) 2026 The EchoWeaverAP Authors

using System;
using System.Collections.Generic;

namespace EchoWeaverAP.Randomizer;

/// <summary>
/// Interface for item randomization.
/// </summary>
public interface IRandomizer
{
    /// <summary>
    /// Whether the Randomizer instance is initialized and ready for the game to start.
    /// </summary>
    bool Initialized { get; }

    /// <summary>
    /// Fires when the Randomizer is initialized.
    /// </summary>
    event Action OnInitialized;

    /// <summary>
    /// Fires when an item has been received. `LocationId` will be set if item originated from a location in the current world. Duplicate events are possible.
    /// </summary>
    event Action<(long ItemId, long? LocationId)> OnItemReceived;

    /// <summary>
    /// Fires when a location has been checked remotely (e.g. due to `!collect` being run on the server). Duplicate events are possible.
    /// </summary>
    event Action<long> OnLocationCheckedRemotely;

    /// <summary>
    /// Records that a location has been checked.
    /// </summary>
    void MarkLocationChecked(long locationId);

    /// <summary>
    /// Populates the item found at a location (if randomized).
    /// </summary>
    bool TryPeekLocation(long locationId, out long itemId, out ItemImportance importance, out string description);

    /// <summary>
    /// Populates the randomized passcodes and buttons for a keypad.
    /// </summary>
    bool TryGetKeypadInfo(KeypadInstance instance, out List<int>[] passcodes, out (Digit Digit, D8Symmetry Symmetry)[] buttons);

    /// <summary>
    /// Populates the randomized frequency code for Tetsa's lab. index should be 0 (101), 1 (102), or 2 (103).
    /// </summary>
    bool TryGetFrequencyCode(int index, out int[] code);

    /// <summary>
    /// Marks a goal as complete.
    /// </summary>
    void SetGoalAchieved(Goal goal);

    /// <summary>
    /// Disconnects from the server and resets all persisted state.
    /// </summary>
    void Reset();
}