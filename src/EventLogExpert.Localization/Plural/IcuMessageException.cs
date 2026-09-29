// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Localization.Plural;

public sealed class IcuMessageException : Exception
{
    public IcuMessageException(string message)
        : base(message) { }

    public IcuMessageException(string message, Exception innerException)
        : base(message, innerException) { }
}
