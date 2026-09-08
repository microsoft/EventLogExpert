// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Database;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Banner;

internal static partial class BannerContentLocalizer
{
    private static string FormatDatabaseFailureSummary(
        IStringLocalizer<SharedResource> localizer,
        IReadOnlyList<ImportFailure> failures,
        IReadOnlyList<ImportFailure> upgradeFailures)
    {
        var parts = new List<string>(failures.Count + upgradeFailures.Count);

        foreach (var failure in failures)
        {
            parts.Add(localizer["Banner_Db_Import_FailurePart", failure.FileName, failure.Reason]);
        }

        foreach (var failure in upgradeFailures)
        {
            parts.Add(localizer["Banner_Db_Import_UpgradeFailurePart", failure.FileName, failure.Reason]);
        }

        return localizer["Banner_Db_Import_FailureSummary", JoinLocalizedList(localizer, parts)];
    }

    private static string ResolveDatabaseOperationNoun(
        IStringLocalizer<SharedResource> localizer,
        DatabaseOperation operation) =>
        operation switch
        {
            DatabaseOperation.Toggle toggle => localizer["Banner_Db_OperationNoun_Toggle", toggle.FileName],
            DatabaseOperation.Import => localizer["Banner_Db_OperationNoun_Import"],
            DatabaseOperation.UpgradeSingle upgradeSingle => localizer[
                "Banner_Db_OperationNoun_UpgradeSingle",
                upgradeSingle.FileName],
            DatabaseOperation.UpgradeBatch { Count: 1 } upgradeBatch => localizer[
                "Banner_Db_OperationNoun_UpgradeBatch_One",
                upgradeBatch.Count],
            DatabaseOperation.UpgradeBatch upgradeBatch => localizer[
                "Banner_Db_OperationNoun_UpgradeBatch_Many",
                upgradeBatch.Count],
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation.GetType(), "Unknown database operation.")
        };

    private static BannerContentText ResolveDatabaseImportSummary(
        IStringLocalizer<SharedResource> localizer,
        DatabaseImportSummary content)
    {
        if (content.Failures.Count == 0 && content.UpgradeFailures.Count == 0)
        {
            return new BannerContentText(
                localizer["Banner_Db_Import_Success_Title"],
                content.Imported == 0 ? localizer["Banner_Db_Import_None"] :
                content.Imported == 1 ? localizer["Banner_Db_Import_Success_One"] :
                localizer["Banner_Db_Import_Success_Many", content.Imported]);
        }

        string failureSummary = FormatDatabaseFailureSummary(localizer, content.Failures, content.UpgradeFailures);

        if (content.Imported == 0)
        {
            return new BannerContentText(
                localizer["Banner_Db_Import_Failed_Title"],
                localizer["Banner_Db_Import_Failed_Message", failureSummary]);
        }

        string partialMessage = content.Imported == 1
            ? localizer["Banner_Db_Import_Partial_One"]
            : localizer["Banner_Db_Import_Partial_Many", content.Imported];

        return new BannerContentText(
            localizer["Banner_Db_Import_Partial_Title"],
            localizer["Banner_Db_Import_Partial_Message", partialMessage, failureSummary]);
    }

    private static BannerContentText ResolveDatabaseOperationFailed(
        IStringLocalizer<SharedResource> localizer,
        DatabaseOperationFailed content)
    {
        string title = content.Operation switch
        {
            DatabaseOperation.Toggle => localizer["Banner_Db_UpdateFailed_Title"],
            DatabaseOperation.Import => localizer["Banner_Db_Import_Failed_Title"],
            DatabaseOperation.UpgradeSingle or DatabaseOperation.UpgradeBatch => localizer["Banner_Db_UpgradeFailed_Title"],
            _ => throw new ArgumentOutOfRangeException(
                nameof(content),
                content.Operation.GetType(),
                "Unknown database operation.")
        };

        return new BannerContentText(
            title,
            localizer["Banner_Db_OperationFailed_Message", ResolveDatabaseOperationNoun(localizer, content.Operation), content.Detail]);
    }
}
