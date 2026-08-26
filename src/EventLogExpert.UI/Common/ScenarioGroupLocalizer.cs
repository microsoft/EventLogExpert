// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Scenarios.Catalog;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Common;

/// <summary>
///     Localizes <see cref="ScenarioGroup" /> display names for the Dashboard. Shared by the category tabs (
///     <c>EmptyStateDashboard</c>) and the scenario-detail eyebrow (<c>ScenarioDetail</c>), which live in different
///     components, so this cannot be a private member of either. The English values mirror
///     <see cref="ScenarioGroupDisplay.DisplayName" /> (a drift-guard test pins them equal).
/// </summary>
internal static class ScenarioGroupLocalizer
{
    internal static string GroupDisplay(IStringLocalizer<SharedResource> localizer, ScenarioGroup group) =>
        localizer[$"Dashboard_Group_{group}"];
}
