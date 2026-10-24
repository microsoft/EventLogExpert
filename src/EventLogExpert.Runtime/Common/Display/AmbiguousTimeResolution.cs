// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Common.Display;

/// <summary>Selects which instant a wall-clock reading repeated by a fall-back transition maps to.</summary>
public enum AmbiguousTimeResolution
{
    Earliest,
    Latest
}
