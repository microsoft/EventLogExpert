// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Library.Helpers;

public interface ITraceLogger
{
    void Trace(string message);
}

public class DebugLogger : ITraceLogger
{
    private Action<string> _tracer;

    public DebugLogger(Action<string> tracer)
    {
        _tracer = tracer;
    }

    public void Trace(string message)
    {
        _tracer(message);
    }
}
