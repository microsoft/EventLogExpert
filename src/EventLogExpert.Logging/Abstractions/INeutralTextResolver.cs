// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Logging.Abstractions;

public interface INeutralTextResolver
{
    string Resolve(string key, IReadOnlyList<string> args);

    /// <summary>
    ///     Resolves a key whose message may be a plural pattern selected by <paramref name="pluralCount" />. The default
    ///     implementation ignores the count and delegates to the two-argument overload, so existing implementers keep working;
    ///     only a resolver that understands the ICU plural mechanism overrides this.
    /// </summary>
    string Resolve(string key, IReadOnlyList<string> args, long? pluralCount) => Resolve(key, args);
}
