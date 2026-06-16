// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Common;
using Microsoft.AspNetCore.Components;
using System.Reflection;

namespace EventLogExpert.UI.Tests.Common;

public sealed class ComponentIdParameterStabilityTests
{
    [Fact]
    public void NoBlazorComponentParameterIsTypedComponentId()
    {
        var assembly = typeof(ComponentId).Assembly;

        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types;
        }

        var offenders = types
            .Where(type => type is not null && typeof(ComponentBase).IsAssignableFrom(type))
            .SelectMany(type => type!.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            .Where(property =>
                property.GetCustomAttribute<ParameterAttribute>() is not null &&
                (property.PropertyType == typeof(ComponentId) || property.PropertyType == typeof(ComponentId?)))
            .Select(property => $"{property.DeclaringType!.Name}.{property.Name}")
            .ToList();

        Assert.Empty(offenders);
    }
}
