// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Common.Sources;

public interface IChangeNotifier
{
    event Action Changed;
}
