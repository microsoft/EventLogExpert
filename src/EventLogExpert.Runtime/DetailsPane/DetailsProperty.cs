// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.DetailsPane;

/// <summary>A single label / value row in the reader view's identity header or system-details section.</summary>
public readonly record struct DetailsProperty(string Label, string Value);
