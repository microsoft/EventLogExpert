// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace EventLogExpert.Test.Services;

public class TestTraceLogger : ITraceLogger
{
    private ITestOutputHelper _outputHelper;

    public TestTraceLogger(ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
    }

    public void Trace(string message, LogLevel level)
    {
        _outputHelper.WriteLine(message);
    }
}
