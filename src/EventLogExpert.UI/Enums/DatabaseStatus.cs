// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI;

public enum DatabaseStatus
{
    NotClassified,
    Ready,
    UpgradeRequired,
    UpgradeFailed,
    UnrecognizedSchema,
    ObsoleteSchema,
    ClassificationFailed
}
