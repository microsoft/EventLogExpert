// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;

namespace EventLogExpert.UI.EventLog;

public sealed record LogReopenInfo(string Name, LogPathType Type);
