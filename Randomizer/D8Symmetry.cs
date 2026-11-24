// SPDX-License-Identifier: BSD-3-Clause
// Copyright (c) 2026 The EchoWeaverAP Authors

namespace EchoWeaverAP.Randomizer;

/// <summary>
/// Symmetry groups in the dihedral group of order 8, representing rotations and flips of a square.
/// </summary>
public enum D8Symmetry
{
    Identity = 0,
    R90,
    R180,
    R270,
    S,
    SR90,
    SR180,
    SR270,
}