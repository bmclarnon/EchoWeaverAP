// SPDX-License-Identifier: BSD-3-Clause
// Copyright (c) 2026 The EchoWeaverAP Authors

using System;
using System.Collections.Generic;

namespace EchoWeaverAP.Randomizer;

public class TestRandomizer : IRandomizer
{
    public bool Initialized { get; } = true;
    public event Action OnInitialized { add { } remove { } }
    public event Action<(long ItemId, long? LocationId)> OnItemReceived { add { } remove { } }
    public event Action<long> OnLocationCheckedRemotely { add { } remove { } }

    public void MarkLocationChecked(long locationId) { }

    public bool TryPeekLocation(long locationId, out long itemId, out ItemImportance importance, out string description)
    {
        switch (locationId)
        {
            default:
                itemId = default;
                importance = default;
                description = default;
                return false;
        }
    }

    public bool TryGetKeypadInfo(KeypadInstance instance, out List<int>[] passcodes, out (Digit Digit, D8Symmetry Symmetry)[] buttons)
    {
        if (instance == KeypadInstance.GlaiveElevator)
        {
            passcodes = [[0, 8, 9, 6]];
            buttons = [
                (Digit.One, D8Symmetry.Identity), (Digit.Two, D8Symmetry.Identity), (Digit.Three, D8Symmetry.Identity),
                (Digit.Four, D8Symmetry.Identity), (Digit.Five, D8Symmetry.Identity), (Digit.Six, D8Symmetry.Identity),
                (Digit.Seven, D8Symmetry.Identity), (Digit.Eight, D8Symmetry.Identity), (Digit.Nine, D8Symmetry.Identity),
                (Digit.Start, D8Symmetry.S),
            ];
            return true;
        }

        passcodes = default;
        buttons = default;
        return false;
    }

    public bool TryGetFrequencyCode(int index, out int[] code)
    {
        switch (index)
        {
            case 0:
                code = [1, 2, 3, 4];
                return true;

            case 1:
                code = [2, 4, 6, 8];
                return true;

            case 2:
                code = [3, 5, 7, 9];
                return true;

            default:
                code = default;
                return false;
        }
    }

    public void SetGoalAchieved(Goal goal) { }

    public void Reset() { }
}