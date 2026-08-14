// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Resolvers;

/// <summary>
///     A single event yielded by <see cref="IEventXmlBatchScanner" />: its record id paired with the freshly rendered
///     event XML.
/// </summary>
internal readonly record struct ScannedEventXml(long RecordId, string Xml);
