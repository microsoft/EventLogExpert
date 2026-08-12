// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace EventLogExpert.Runtime.Tests.Architecture;

public sealed class RuntimeToolkitNeutralityTests
{
    private static readonly string[] s_allowedAspNetCoreReferences = [];

    [Fact]
    public void Runtime_ReferencesNoAspNetCoreTypesBeyondTheAllowedSet()
    {
        string[] actual = [.. ReadTypeReferences()
            .Where(reference => IsUnderRoot(reference.Namespace, "Microsoft.AspNetCore"))
            .Select(reference => reference.FullName)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

        Assert.Equal([.. s_allowedAspNetCoreReferences.Order(StringComparer.Ordinal)], actual);
    }

    [Fact]
    public void Scanner_DetectsReferencesThatAreKnownToBePresent()
    {
        IReadOnlyList<(string Namespace, string FullName)> references = ReadTypeReferences();

        Assert.Contains(references, reference => IsUnderRoot(reference.Namespace, "Fluxor"));
        Assert.Contains(references, reference => IsUnderRoot(reference.Namespace, "Microsoft.Extensions"));
    }

    private static bool IsUnderRoot(string candidateNamespace, string root) =>
        string.Equals(candidateNamespace, root, StringComparison.Ordinal) ||
        candidateNamespace.StartsWith(root + ".", StringComparison.Ordinal);

    private static IReadOnlyList<(string Namespace, string FullName)> ReadTypeReferences()
    {
        string assemblyPath = typeof(IAlertDialogService).Assembly.Location;

        Assert.True(File.Exists(assemblyPath), $"Expected EventLogExpert.Runtime at '{assemblyPath}'.");

        using FileStream stream = File.OpenRead(assemblyPath);
        using PEReader peReader = new(stream);
        MetadataReader reader = peReader.GetMetadataReader();

        List<(string Namespace, string FullName)> references = new(reader.TypeReferences.Count);

        foreach (TypeReferenceHandle handle in reader.TypeReferences)
        {
            TypeReference typeReference = reader.GetTypeReference(handle);
            string typeNamespace = reader.GetString(typeReference.Namespace);
            string typeName = reader.GetString(typeReference.Name);

            references.Add((typeNamespace,
                typeNamespace.Length == 0 ? typeName : string.Concat(typeNamespace, ".", typeName)));
        }

        return references;
    }
}
