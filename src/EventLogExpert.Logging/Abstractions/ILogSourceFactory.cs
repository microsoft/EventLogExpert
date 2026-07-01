// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Logging.Abstractions;

public interface ILogSourceFactory
{
    ITraceLogger ForCategory(string category);
}
