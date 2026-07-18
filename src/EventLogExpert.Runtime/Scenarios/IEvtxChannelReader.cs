// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Scenarios;

/// <summary>Reads the channel (log name) an exported <c>.evtx</c> file's events belong to.</summary>
internal interface IEvtxChannelReader
{
    EvtxChannelReadResult ReadChannel(string filePath);
}
