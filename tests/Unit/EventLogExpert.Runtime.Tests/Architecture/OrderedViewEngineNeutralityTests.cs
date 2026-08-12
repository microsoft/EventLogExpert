// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.LogTable.OrderedView;
using System.Reflection;

namespace EventLogExpert.Runtime.Tests.Architecture;

public sealed class OrderedViewEngineNeutralityTests
{
    private const string EngineNamespace = "EventLogExpert.Runtime.LogTable.OrderedView";

    private const string FluxorAssembly = "Fluxor";

    private static readonly HashSet<string> s_sanctionedAdapters = new(StringComparer.Ordinal)
    {
        typeof(OrderedViewDispatchBridge).FullName!,
        typeof(OrderedViewShadowEffects).FullName!
    };

    [Fact]
    public void EngineCore_ReferencesNoFluxorTypes()
    {
        var offenders = EngineTypes()
            .Where(type => !s_sanctionedAdapters.Contains(type.FullName ?? type.Name))
            .Select(type => (type, references: FluxorReferences(type)))
            .Where(candidate => candidate.references.Count > 0)
            .Select(candidate => $"{candidate.type.FullName} -> {string.Join(", ", candidate.references)}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Engine-core types must not reference Fluxor. Offenders:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void Scan_CoversTheWholeEngineNamespace()
    {
        Type[] engineTypes = EngineTypes();

        Assert.All(s_sanctionedAdapters, adapter => Assert.Contains(engineTypes, type => (type.FullName ?? type.Name) == adapter));
        Assert.True(
            engineTypes.Length > s_sanctionedAdapters.Count + 5,
            $"Expected the engine namespace to hold many core types beyond its {s_sanctionedAdapters.Count} adapters; found {engineTypes.Length}.");
    }

    [Fact]
    public void Scanner_DetectsFluxorInTheSanctionedAdapters()
    {
        foreach (string adapterName in s_sanctionedAdapters)
        {
            Type adapter = EngineTypes().Single(type => (type.FullName ?? type.Name) == adapterName);

            Assert.NotEmpty(FluxorReferences(adapter));
        }
    }

    private static IEnumerable<Type> AttributeTypes(IEnumerable<CustomAttributeData> attributes) =>
        attributes.Select(attribute => attribute.AttributeType);

    private static void CollectFluxor(Type type, SortedSet<string> found)
    {
        Type element = type;

        while (element.IsArray || element.IsByRef || element.IsPointer)
        {
            element = element.GetElementType()!;
        }

        if (element.IsGenericParameter) { return; }

        if (string.Equals(element.Assembly.GetName().Name, FluxorAssembly, StringComparison.Ordinal))
        {
            found.Add(element.FullName ?? element.Name);
        }

        if (element.IsGenericType)
        {
            foreach (Type argument in element.GetGenericArguments())
            {
                CollectFluxor(argument, found);
            }
        }
    }

    private static Type[] EngineTypes() =>
        [.. typeof(OrderedViewWriter).Assembly
            .GetTypes()
            .Where(type => string.Equals(type.Namespace, EngineNamespace, StringComparison.Ordinal))];

    private static IReadOnlyList<string> FluxorReferences(Type type)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);

        foreach (Type referenced in ReferencedTypes(type))
        {
            CollectFluxor(referenced, found);
        }

        return [.. found];
    }

    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        const BindingFlags Members =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        if (type.BaseType is { } baseType) { yield return baseType; }

        foreach (Type contract in type.GetInterfaces()) { yield return contract; }

        foreach (Type attribute in AttributeTypes(type.GetCustomAttributesData())) { yield return attribute; }

        foreach (FieldInfo field in type.GetFields(Members))
        {
            yield return field.FieldType;

            foreach (Type attribute in AttributeTypes(field.GetCustomAttributesData())) { yield return attribute; }
        }

        foreach (PropertyInfo property in type.GetProperties(Members)) { yield return property.PropertyType; }

        foreach (ConstructorInfo constructor in type.GetConstructors(Members))
        {
            foreach (ParameterInfo parameter in constructor.GetParameters()) { yield return parameter.ParameterType; }
        }

        foreach (MethodInfo method in type.GetMethods(Members))
        {
            yield return method.ReturnType;

            foreach (ParameterInfo parameter in method.GetParameters()) { yield return parameter.ParameterType; }

            foreach (Type attribute in AttributeTypes(method.GetCustomAttributesData())) { yield return attribute; }
        }
    }
}
