// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Provider.Resolution;

/// <summary>
///     One template &lt;data&gt; node as spans: parsed attributes, or a raw element span when it cannot be
///     canonically parsed.
/// </summary>
public readonly ref struct TemplateField
{
    private TemplateField(ReadOnlySpan<char> raw)
    {
        IsRaw = true;
        Raw = raw;
    }

    private TemplateField(
        ReadOnlySpan<char> name,
        ReadOnlySpan<char> inType,
        ReadOnlySpan<char> outType,
        ReadOnlySpan<char> length,
        ReadOnlySpan<char> map)
    {
        Name = name;
        InType = inType;
        OutType = outType;
        Length = length;
        Map = map;
    }

    public bool IsRaw { get; }

    public ReadOnlySpan<char> InType { get; }

    public ReadOnlySpan<char> Length { get; }

    public ReadOnlySpan<char> Map { get; }

    public ReadOnlySpan<char> Name { get; }

    public ReadOnlySpan<char> OutType { get; }

    public ReadOnlySpan<char> Raw { get; }

    public static TemplateField Parsed(
        ReadOnlySpan<char> name,
        ReadOnlySpan<char> inType,
        ReadOnlySpan<char> outType,
        ReadOnlySpan<char> length,
        ReadOnlySpan<char> map) =>
        new(name, inType, outType, length, map);

    public static TemplateField RawElement(ReadOnlySpan<char> element) => new(element);
}
