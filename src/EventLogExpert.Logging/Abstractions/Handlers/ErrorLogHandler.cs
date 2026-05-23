// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace EventLogExpert.Logging.Abstractions.Handlers;

[InterpolatedStringHandler]
public struct ErrorLogHandler
{
    private LogHandlerCore _core;

    public readonly bool IsEnabled => _core.IsEnabled;

    public ErrorLogHandler(int literalLength, int formattedCount, ITraceLogger logger, out bool isEnabled) =>
        _core.Init(literalLength, formattedCount, logger, LogLevel.Error, out isEnabled);

    public void AppendLiteral(string s) => _core.AppendLiteral(s);

    public void AppendFormatted<T>(T value) => _core.AppendFormatted(value);

    public void AppendFormatted(string? value) => _core.AppendFormatted(value);

    public void AppendFormatted(int value) => _core.AppendFormatted(value);

    public void AppendFormatted(long value) => _core.AppendFormatted(value);

    public void AppendFormatted(double value) => _core.AppendFormatted(value);

    public void AppendFormatted<T>(T value, string? format) => _core.AppendFormatted(value, format);

    public void AppendFormatted<T>(T value, int alignment) => _core.AppendFormatted(value, alignment);

    public void AppendFormatted<T>(T value, int alignment, string? format) =>
        _core.AppendFormatted(value, alignment, format);

    public void AppendFormatted(ReadOnlySpan<char> value) => _core.AppendFormatted(value);

    public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string? format = null) =>
        _core.AppendFormatted(value, alignment, format);

    public override readonly string ToString() => _core.ToString();

    public string ToStringAndClear() => _core.ToStringAndClear();
}
