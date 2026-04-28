// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Text;

namespace EventLogExpert.Eventing.Helpers;

internal struct LogHandlerCore
{
    [ThreadStatic] private static StringBuilder? s_pooledBuilder;

    private const int MaxPooledCapacity = 4096;

    private StringBuilder? _builder;

    internal bool IsEnabled;

    internal void Init(int literalLength, int formattedCount, ITraceLogger logger, LogLevel level, out bool isEnabled)
    {
        IsEnabled = isEnabled = level >= logger.MinimumLevel;

        if (!isEnabled) { return; }

        var pooled = s_pooledBuilder;

        if (pooled is not null)
        {
            s_pooledBuilder = null;
            _builder = pooled;
        }
        else
        {
            _builder = new StringBuilder(literalLength + (formattedCount * 8));
        }
    }

    internal void AppendLiteral(string s) => _builder!.Append(s);

    internal void AppendFormatted<T>(T value) => _builder!.Append(value);

    internal void AppendFormatted(string? value) => _builder!.Append(value);

    internal void AppendFormatted(int value) => _builder!.Append(value);

    internal void AppendFormatted(long value) => _builder!.Append(value);

    internal void AppendFormatted(double value) => _builder!.Append(value);

    internal void AppendFormatted<T>(T value, string? format)
    {
        if (format is null) { _builder!.Append(value); return; }
        if (value is IFormattable formattable) { _builder!.Append(formattable.ToString(format, null)); return; }
        _builder!.Append(value);
    }

    internal void AppendFormatted<T>(T value, int alignment) => AppendAligned(value, alignment, null);

    internal void AppendFormatted<T>(T value, int alignment, string? format) => AppendAligned(value, alignment, format);

    internal void AppendFormatted(ReadOnlySpan<char> value) => _builder!.Append(value);

    internal void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string? format = null) =>
        _builder!.Append(value);

    public override readonly string ToString() => _builder?.ToString() ?? string.Empty;

    internal string ToStringAndClear()
    {
        if (_builder is null) { return string.Empty; }

        string result = _builder.ToString();
        var consumed = _builder;
        _builder = null;
        consumed.Clear();
        if (consumed.Capacity <= MaxPooledCapacity) { s_pooledBuilder = consumed; }

        return result;
    }

    private void AppendAligned<T>(T value, int alignment, string? format)
    {
        string formatted = value switch
        {
            null => string.Empty,
            IFormattable f when format is not null => f.ToString(format, null) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };

        int width = alignment == int.MinValue ? int.MaxValue : Math.Abs(alignment);
        int padding = width - formatted.Length;
        if (padding <= 0) { _builder!.Append(formatted); return; }

        if (alignment >= 0) { _builder!.Append(' ', padding).Append(formatted); }
        else { _builder!.Append(formatted).Append(' ', padding); }
    }
}

[InterpolatedStringHandler]
public struct TraceLogHandler
{
    private LogHandlerCore _core;

    public readonly bool IsEnabled => _core.IsEnabled;

    public TraceLogHandler(int literalLength, int formattedCount, ITraceLogger logger, out bool isEnabled) =>
        _core.Init(literalLength, formattedCount, logger, LogLevel.Trace, out isEnabled);

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

[InterpolatedStringHandler]
public struct DebugLogHandler
{
    private LogHandlerCore _core;

    public readonly bool IsEnabled => _core.IsEnabled;

    public DebugLogHandler(int literalLength, int formattedCount, ITraceLogger logger, out bool isEnabled) =>
        _core.Init(literalLength, formattedCount, logger, LogLevel.Debug, out isEnabled);

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

[InterpolatedStringHandler]
public struct InfoLogHandler
{
    private LogHandlerCore _core;

    public readonly bool IsEnabled => _core.IsEnabled;

    public InfoLogHandler(int literalLength, int formattedCount, ITraceLogger logger, out bool isEnabled) =>
        _core.Init(literalLength, formattedCount, logger, LogLevel.Information, out isEnabled);

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

[InterpolatedStringHandler]
public struct WarnLogHandler
{
    private LogHandlerCore _core;

    public readonly bool IsEnabled => _core.IsEnabled;

    public WarnLogHandler(int literalLength, int formattedCount, ITraceLogger logger, out bool isEnabled) =>
        _core.Init(literalLength, formattedCount, logger, LogLevel.Warning, out isEnabled);

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

[InterpolatedStringHandler]
public struct CriticalLogHandler
{
    private LogHandlerCore _core;

    public readonly bool IsEnabled => _core.IsEnabled;

    public CriticalLogHandler(int literalLength, int formattedCount, ITraceLogger logger, out bool isEnabled) =>
        _core.Init(literalLength, formattedCount, logger, LogLevel.Critical, out isEnabled);

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
