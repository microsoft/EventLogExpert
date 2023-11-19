// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace EventLogExpert.Test.Services;

public class TestTraceLogger(ITestOutputHelper outputHelper) : ITraceLogger
{
    public void Trace(string message, LogLevel level) => outputHelper.WriteLine(message);
}
