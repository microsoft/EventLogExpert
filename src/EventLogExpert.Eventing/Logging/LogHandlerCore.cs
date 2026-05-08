// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using System.Text;

namespace EventLogExpert.Eventing.Logging;

internal struct LogHandlerCore
{
    [ThreadStatic] private static StringBuilder? t_pooledBuilder;

    private const int MaxPooledCapacity = 4096;

    private StringBuilder? _builder;

    internal bool IsEnabled;

    internal void Init(int literalLength, int formattedCount, ITraceLogger logger, LogLevel level, out bool isEnabled)
    {
        IsEnabled = isEnabled = level >= logger.MinimumLevel;

        if (!isEnabled) { return; }

        var pooled = t_pooledBuilder;

        if (pooled is not null)
        {
            t_pooledBuilder = null;
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

    internal void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string? format = null)
    {
        if (alignment == 0) { _builder!.Append(value); return; }

        int width = alignment == int.MinValue ? int.MaxValue : Math.Abs(alignment);
        int padding = width - value.Length;

        if (padding <= 0) { _builder!.Append(value); return; }

        if (alignment >= 0) { _builder!.Append(' ', padding).Append(value); }
        else { _builder!.Append(value).Append(' ', padding); }
    }

    public override readonly string ToString() => _builder?.ToString() ?? string.Empty;

    internal string ToStringAndClear()
    {
        if (_builder is null) { return string.Empty; }

        string result = _builder.ToString();
        var consumed = _builder;
        _builder = null;

        consumed.Clear();

        if (consumed.Capacity <= MaxPooledCapacity) { t_pooledBuilder = consumed; }

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
