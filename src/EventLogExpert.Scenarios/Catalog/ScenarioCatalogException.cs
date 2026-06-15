// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Scenarios.Catalog;

/// <summary>Thrown when the scenario catalog fails to load; carries every error found.</summary>
public sealed class ScenarioCatalogException : Exception
{
    public ScenarioCatalogException(ImmutableList<string> errors)
        : base(BuildMessage(errors)) =>
        Errors = errors;

    public ScenarioCatalogException()
        : this([]) { }

    public ScenarioCatalogException(string message)
        : base(message) =>
        Errors = [message];

    public ScenarioCatalogException(string message, Exception innerException)
        : base(message, innerException) =>
        Errors = [message];

    public ImmutableList<string> Errors { get; } = [];

    private static string BuildMessage(ImmutableList<string> errors) =>
        errors.IsEmpty
            ? "The built-in scenario catalog failed to load."
            : $"The built-in scenario catalog has {errors.Count} error(s):{Environment.NewLine}" +
              string.Join(Environment.NewLine, errors.Select(error => $"  - {error}"));
}
