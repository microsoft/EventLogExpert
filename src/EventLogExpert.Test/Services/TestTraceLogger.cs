// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using Xunit.Abstractions;

namespace EventLogExpert.Test.Services;

public class TestTraceLogger : ITraceLogger
{
    private ITestOutputHelper _outputHelper;

    public TestTraceLogger(ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
    }

    public void Trace(string message)
    {
        _outputHelper.WriteLine(message);
    }
}
