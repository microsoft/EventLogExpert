﻿// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using System.Diagnostics.Eventing.Reader;

namespace EventLogExpert.Eventing.EventResolvers;

/// <summary>
/// Turns a System.Diagnostics.Eventing.Reader.EventRecord into an EventLogExpert.Library.Models.DisplayEventModel.
/// </summary>
public interface IEventResolver : IDisposable
{
    public DisplayEventModel Resolve(EventRecord eventRecord, string owningLogName);

    public string Status { get; }
}
