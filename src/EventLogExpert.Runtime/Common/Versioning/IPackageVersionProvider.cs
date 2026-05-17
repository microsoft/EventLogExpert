// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Common.Versioning;

public interface IPackageVersionProvider
{
    Version GetPackageVersion();
}
