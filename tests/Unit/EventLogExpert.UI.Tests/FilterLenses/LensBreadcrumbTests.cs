// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using AngleSharp.Dom;
using Bunit;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.UI.FilterLenses;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using NSubstitute;
using System.Collections.Immutable;
using System.Reflection;

namespace EventLogExpert.UI.Tests.FilterLenses;

public sealed class LensBreadcrumbTests : BunitContext
{
    private static readonly string FilterPaneSelector = (string)typeof(LensBreadcrumb)
        .GetField("FilterPaneFocusSelector", BindingFlags.NonPublic | BindingFlags.Static)!
        .GetRawConstantValue()!;
    private readonly IAlertDialogService _alertDialog = Substitute.For<IAlertDialogService>();
    private readonly IAnnouncementService _announcements = Substitute.For<IAnnouncementService>();
    private readonly IFilterLensCommands _commands = Substitute.For<IFilterLensCommands>();
    private readonly IFilterLensSource _source = Substitute.For<IFilterLensSource>();

    public LensBreadcrumbTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _source.Lenses.Returns(ImmutableList<FilterLensSummary>.Empty);
        Services.AddSingleton(_commands);
        Services.AddSingleton(_source);
        Services.AddSingleton(_alertDialog);
        Services.AddSingleton(_announcements);
        Services.AddEventLogLocalization();
    }

    private IStringLocalizer<SharedResource> Localizer =>
        Services.GetRequiredService<IStringLocalizer<SharedResource>>();

    [Fact]
    public void Changed_ReRendersTheBreadcrumb()
    {
        var cut = Render<LensBreadcrumb>();
        Assert.Empty(cut.FindAll(".lens-breadcrumb"));

        _source.Lenses.Returns(ImmutableList.Create(Summary("abc")));
        _source.Changed += Raise.Event<Action>();

        cut.WaitForAssertion(() => Assert.Contains("Activity ID = abc", cut.Markup));
    }

    [Fact]
    public void ClearAllButton_DispatchesClearLenses()
    {
        _source.Lenses.Returns(ImmutableList.Create(Summary("x")));

        var cut = Render<LensBreadcrumb>();

        cut.Find(".lens-clear").Click();

        _commands.Received(1).ClearLenses();
    }

    [Fact]
    public void ClearAll_RestoresFocusToFilterPane()
    {
        _source.Lenses.Returns(ImmutableList.Create(Summary("a"), Summary("b")));
        var focusModule = JSInterop.SetupModule("./_content/EventLogExpert.UI/Common/focusGuard.js");
        // Clear all empties the stack synchronously (Fluxor reducer).
        _commands.When(commands => commands.ClearLenses())
            .Do(_ => _source.Lenses.Returns(ImmutableList<FilterLensSummary>.Empty));

        var cut = Render<LensBreadcrumb>();
        cut.Find(".lens-clear").Click();
        _commands.Received(1).ClearLenses();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".lens-breadcrumb")));
        AssertFilterPaneRestore(focusModule);
    }

    [Fact]
    public void Escape_GuardUnavailable_FailsClosed_MovesNoFocus()
    {
        var older = Summary("older");
        var top = Summary("top");
        _source.Lenses.Returns(ImmutableList.Create(older, top));
        var focusModule = JSInterop.SetupModule("./_content/EventLogExpert.UI/Common/focusGuard.js");
        focusModule.Setup<bool>("focusIfNotElsewhere", _ => true).SetException(new JSException("boom"));

        var cut = Render<LensBreadcrumb>();
        cut.Find(".lens-breadcrumb").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        _source.Lenses.Returns(ImmutableList.Create(older));
        _source.Changed += Raise.Event<Action>();

        // The guard threw: Escape must fail CLOSED - no bare FocusAsync fallback that could steal focus from the region.
        cut.WaitForAssertion(() => focusModule.VerifyInvoke("focusIfNotElsewhere"));
        Assert.DoesNotContain(JSInterop.Invocations, invocation =>
            invocation.Identifier.Contains("domWrapper.focus", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Escape_PopsTopLens_RestoresPreviousChipThroughOrphanGuard()
    {
        var older = Summary("older");
        var top = Summary("top");
        _source.Lenses.Returns(ImmutableList.Create(older, top));
        var focusModule = JSInterop.SetupModule("./_content/EventLogExpert.UI/Common/focusGuard.js");

        var cut = Render<LensBreadcrumb>();
        cut.Find(".lens-breadcrumb").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        _commands.Received(1).RemoveLens(top.Id);

        // Removal propagates: Escape's restore routes through the orphan guard (never a bare FocusAsync); the guard's
        // decision is unit-tested in focusGuard.test.js.
        _source.Lenses.Returns(ImmutableList.Create(older));
        _source.Changed += Raise.Event<Action>();

        cut.WaitForAssertion(() => focusModule.VerifyInvoke("focusIfNotElsewhere"));
    }

    [Fact]
    public void Escape_RemovingOnlyLens_RestoresFocusToFilterPane()
    {
        var only = Summary("only");
        _source.Lenses.Returns(ImmutableList.Create(only));
        var focusModule = JSInterop.SetupModule("./_content/EventLogExpert.UI/Common/focusGuard.js");

        var cut = Render<LensBreadcrumb>();
        cut.Find(".lens-breadcrumb").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        _commands.Received(1).RemoveLens(only.Id);

        _source.Lenses.Returns(ImmutableList<FilterLensSummary>.Empty);
        _source.Changed += Raise.Event<Action>();

        // All Escape restores fail CLOSED: the module import await is a window for focus to move, and the filters pane
        // is only reachable through the guard module, so an unguarded restore has no fail-open benefit, only steal risk.
        AssertFilterPaneRestore(focusModule);
    }

    [Fact]
    public void Escape_WithinBreadcrumb_PopsTopLens()
    {
        var older = Summary("older");
        var top = Summary("top");
        _source.Lenses.Returns(ImmutableList.Create(older, top));

        var cut = Render<LensBreadcrumb>();

        cut.Find(".lens-breadcrumb").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        _commands.Received(1).RemoveLens(top.Id);
    }

    [Fact]
    public void KeepButton_Click_RemovingOnlyLens_RestoresFocusToFilterPane()
    {
        var only = Summary("only");
        _source.Lenses.Returns(ImmutableList.Create(only));
        var focusModule = JSInterop.SetupModule("./_content/EventLogExpert.UI/Common/focusGuard.js");

        var cut = Render<LensBreadcrumb>();

        cut.Find(".lens-chip-keep").Click();
        _commands.Received(1).PromoteLens(only.Id);

        _source.Lenses.Returns(ImmutableList<FilterLensSummary>.Empty);
        _source.Changed += Raise.Event<Action>();

        // Sole-lens keep unmounts the region; restore (guarded) to the filters pane, never a (now gone) remove button.
        AssertFilterPaneRestore(focusModule);
    }

    [Fact]
    public void KeepButton_Click_RestoresFocusToNeighborKeepButton()
    {
        var a = Summary("a");
        var b = Summary("b");
        var c = Summary("c");
        _source.Lenses.Returns(ImmutableList.Create(a, b, c));

        var cut = Render<LensBreadcrumb>();

        // Promote (keep) the middle chip; its keep button holds focus and unmounts once the promotion propagates.
        cut.FindAll(".lens-chip-keep")[1].Click();
        _commands.Received(1).PromoteLens(b.Id);

        _source.Lenses.Returns(ImmutableList.Create(a, c));
        _source.Changed += Raise.Event<Action>();

        // Restore to the neighbor's KEEP button (not its remove ×), so keeping lenses in sequence never lands on delete.
        cut.WaitForAssertion(() => AssertFocusRestoredToChipButton(cut, c.Id, "_keepRefs"));
    }

    [Fact]
    public void KeepButton_DispatchesPromoteLens_AndHasAccessibleLabel()
    {
        var lens = Summary("abc");
        _source.Lenses.Returns(ImmutableList.Create(lens));

        var cut = Render<LensBreadcrumb>();

        var keep = cut.Find(".lens-chip-keep");
        Assert.Equal("Save lens as filter: Activity ID = abc", keep.GetAttribute("aria-label"));

        keep.Click();

        _commands.Received(1).PromoteLens(lens.Id);
    }

    [Fact]
    public void NoLenses_RendersNothing()
    {
        _source.Lenses.Returns(ImmutableList<FilterLensSummary>.Empty);

        var cut = Render<LensBreadcrumb>();

        Assert.Empty(cut.FindAll(".lens-breadcrumb"));
    }

    [Fact]
    public void RemoveChip_Click_RemovingOnlyLens_RestoresFocusToFilterPane()
    {
        var only = Summary("only");
        _source.Lenses.Returns(ImmutableList.Create(only));
        var focusModule = JSInterop.SetupModule("./_content/EventLogExpert.UI/Common/focusGuard.js");

        var cut = Render<LensBreadcrumb>();

        cut.Find(".lens-chip-remove").Click();
        _commands.Received(1).RemoveLens(only.Id);

        // Removal propagates to empty: the region unmounts, so restore (guarded) to the filters pane.
        _source.Lenses.Returns(ImmutableList<FilterLensSummary>.Empty);
        _source.Changed += Raise.Event<Action>();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".lens-breadcrumb")));
        AssertFilterPaneRestore(focusModule);
    }

    [Fact]
    public void RemoveChip_Click_RestoresFocusToNeighbor_OnceRemovalPropagates()
    {
        var a = Summary("a");
        var b = Summary("b");
        var c = Summary("c");
        _source.Lenses.Returns(ImmutableList.Create(a, b, c));

        var cut = Render<LensBreadcrumb>();

        // Remove the middle chip (b); its button holds focus and will unmount once the removal propagates.
        cut.FindAll(".lens-chip-remove")[1].Click();
        _commands.Received(1).RemoveLens(b.Id);

        // The source has not changed yet (removal in flight): the arm must NOT fire while the chip is still present.
        Assert.DoesNotContain(JSInterop.Invocations, invocation =>
            invocation.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase));

        // Removal propagates: b is gone. The deferred restore fires once, moving focus to the forward neighbor (c).
        _source.Lenses.Returns(ImmutableList.Create(a, c));
        _source.Changed += Raise.Event<Action>();

        cut.WaitForAssertion(() => AssertFocusRestoredTo(cut, c.Id));
    }

    [Fact]
    public void SaveAllButton_PromotesAllLenses()
    {
        _source.Lenses.Returns(ImmutableList.Create(Summary("a"), Summary("b")));

        var cut = Render<LensBreadcrumb>();

        SaveActionButton(cut, Localizer["FilterLens_SaveAll"].Value).Click();

        _commands.Received(1).PromoteAllLenses();

        // The "saved all" announcement now originates from the promote effect (after the commit), not the
        // breadcrumb, so the breadcrumb must not announce anything itself.
        _announcements.DidNotReceive().Announce(Arg.Any<string>());
    }

    [Fact]
    public void SaveAll_WhenLensesEmpty_RestoresFocusToFilterPane()
    {
        _source.Lenses.Returns(ImmutableList.Create(Summary("a"), Summary("b")));
        var focusModule = JSInterop.SetupModule("./_content/EventLogExpert.UI/Common/focusGuard.js");
        // Promote-all empties the stack synchronously (Fluxor reducer), so the post-click render already unmounts and
        // the bulk arm consumes rather than disarming as a no-op.
        _commands.When(commands => commands.PromoteAllLenses())
            .Do(_ => _source.Lenses.Returns(ImmutableList<FilterLensSummary>.Empty));

        var cut = Render<LensBreadcrumb>();
        SaveActionButton(cut, Localizer["FilterLens_SaveAll"].Value).Click();
        _commands.Received(1).PromoteAllLenses();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".lens-breadcrumb")));
        AssertFilterPaneRestore(focusModule);
    }

    [Fact]
    public void SaveAll_WhenPromoteDoesNotEmpty_DisarmsBeforeAnUnrelatedLaterEmpty()
    {
        _source.Lenses.Returns(ImmutableList.Create(Summary("a"), Summary("b")));
        var focusModule = JSInterop.SetupModule("./_content/EventLogExpert.UI/Common/focusGuard.js");

        var cut = Render<LensBreadcrumb>();
        SaveActionButton(cut, Localizer["FilterLens_SaveAll"].Value).Click();

        // A render where the promote did NOT empty the list disarms the synchronous bulk arm (it is a no-op, no orphan).
        _source.Changed += Raise.Event<Action>();

        // So a later, unrelated emptying (e.g. closing the last log) must NOT yank focus into the pane.
        _source.Lenses.Returns(ImmutableList<FilterLensSummary>.Empty);
        _source.Changed += Raise.Event<Action>();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".lens-breadcrumb")));
        Assert.Empty(focusModule.Invocations["focusSelectorIfNotElsewhere"]);
    }

    [Fact]
    public async Task SaveAsGroupButton_SuppliesWhitespaceRejectingValidatorToPrompt()
    {
        // Without a validator, clearing the name and pressing save closes the dialog and the outcome is silently
        // discarded. The prompt must reject blank names (keeping itself open) like the tab-group prompts do.
        Func<string, string?>? capturedValidator = null;
        _source.Lenses.Returns(ImmutableList.Create(Summary("a")));
        _alertDialog.DisplayPromptWithSecondary(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Do<Func<string, string?>?>(validator => capturedValidator = validator))
            .Returns(new PromptOutcome(PromptChoice.Cancel, string.Empty));

        var cut = Render<LensBreadcrumb>();

        await SaveActionButton(cut, Localizer["FilterLens_SaveAsGroup"].Value).ClickAsync(new MouseEventArgs());

        Assert.NotNull(capturedValidator);
        Assert.False(string.IsNullOrEmpty(capturedValidator!("   ")));
        Assert.Null(capturedValidator!("Valid name"));
    }

    [Fact]
    public async Task SaveAsGroupButton_WhenAriaDisabled_ActivationIsNoOp()
    {
        // aria-disabled (unlike the native disabled attribute) does NOT block activation in the browser, so the
        // SaveAsGroupAsync guard is now the sole protection. Lock it in: activating the unavailable button must not
        // prompt or save.
        _source.Lenses.Returns(ImmutableList.Create(TimeSummary()));

        var cut = Render<LensBreadcrumb>();
        var button = SaveActionButton(cut, Localizer["FilterLens_SaveAsGroup"].Value);

        await button.ClickAsync(new MouseEventArgs());

        await _alertDialog.DidNotReceive().DisplayPromptWithSecondary(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Func<string, string?>?>());
        _commands.DidNotReceive().SaveLensesAsGroup(Arg.Any<string>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task SaveAsGroupButton_WhenPromptReturnsCancel_DoesNotSaveOrClear()
    {
        _source.Lenses.Returns(ImmutableList.Create(Summary("a")));
        _alertDialog.DisplayPromptWithSecondary(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Func<string, string?>?>())
            .Returns(new PromptOutcome(PromptChoice.Cancel, string.Empty));

        var cut = Render<LensBreadcrumb>();

        await SaveActionButton(cut, Localizer["FilterLens_SaveAsGroup"].Value).ClickAsync(new MouseEventArgs());

        _commands.DidNotReceive().SaveLensesAsGroup(Arg.Any<string>(), Arg.Any<bool>());
        _commands.DidNotReceive().ClearLenses();
    }

    [Fact]
    public async Task SaveAsGroupButton_WhenPromptReturnsDefaultOutcome_DoesNotSaveOrClear()
    {
        // A forced modal close or host teardown completes the standalone prompt with default(PromptOutcome),
        // whose Value is null. The breadcrumb must treat it as a cancel and never dereference the null name.
        _source.Lenses.Returns(ImmutableList.Create(Summary("a")));
        _alertDialog.DisplayPromptWithSecondary(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Func<string, string?>?>())
            .Returns(default(PromptOutcome));

        var cut = Render<LensBreadcrumb>();

        await SaveActionButton(cut, Localizer["FilterLens_SaveAsGroup"].Value).ClickAsync(new MouseEventArgs());

        _commands.DidNotReceive().SaveLensesAsGroup(Arg.Any<string>(), Arg.Any<bool>());
        _commands.DidNotReceive().ClearLenses();
    }

    [Fact]
    public async Task SaveAsGroupButton_WhenPromptReturnsPrimary_SavesGroupWithoutClearing()
    {
        _source.Lenses.Returns(ImmutableList.Create(Summary("a")));
        _alertDialog.DisplayPromptWithSecondary(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Func<string, string?>?>())
            .Returns(new PromptOutcome(PromptChoice.Primary, "My Group"));

        var cut = Render<LensBreadcrumb>();

        var button = SaveActionButton(cut, Localizer["FilterLens_SaveAsGroup"].Value);
        Assert.False(button.HasAttribute("disabled"));
        Assert.Equal("false", button.GetAttribute("aria-disabled"));
        Assert.False(button.HasAttribute("aria-describedby"));

        await button.ClickAsync(new MouseEventArgs());

        _commands.Received(1).SaveLensesAsGroup("My Group", clearAfterSave: false);
        _commands.DidNotReceive().ClearLenses();

        // Success is announced from the lens effect only after the write actually persists (failure surfaces via
        // the error banner), so the breadcrumb no longer announces optimistically on click.
        _announcements.DidNotReceive().Announce(Arg.Any<string>());
    }

    [Fact]
    public async Task SaveAsGroupButton_WhenPromptReturnsSecondary_SavesGroupWithClearAfterSave()
    {
        _source.Lenses.Returns(ImmutableList.Create(Summary("a")));
        _alertDialog.DisplayPromptWithSecondary(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Func<string, string?>?>())
            .Returns(new PromptOutcome(PromptChoice.Secondary, "My Group"));

        var cut = Render<LensBreadcrumb>();

        await SaveActionButton(cut, Localizer["FilterLens_SaveAsGroup"].Value).ClickAsync(new MouseEventArgs());

        // Save and clear defers the clear to the persist-success effect, so the breadcrumb itself never calls
        // ClearLenses directly - it flags the save with clearAfterSave: true.
        _commands.Received(1).SaveLensesAsGroup("My Group", clearAfterSave: true);
        _commands.DidNotReceive().ClearLenses();
    }

    [Fact]
    public void SaveAsGroupButton_WithOnlyTimeWindowLenses_IsAriaDisabled_WithAccessibleReason()
    {
        _source.Lenses.Returns(ImmutableList.Create(TimeSummary()));

        var cut = Render<LensBreadcrumb>();

        var button = SaveActionButton(cut, Localizer["FilterLens_SaveAsGroup"].Value);

        // Unavailable via aria-disabled, NOT the native disabled attribute (which would drop keyboard focus), so a
        // screen-reader user can still reach the button and hear why it is unavailable.
        Assert.Equal("true", button.GetAttribute("aria-disabled"));
        Assert.False(button.HasAttribute("disabled"));

        // The reason is exposed as a real accessible description (aria-describedby -> visually-hidden text), not just
        // a title tooltip that assistive tech does not reliably announce.
        var hintId = button.GetAttribute("aria-describedby");
        Assert.False(string.IsNullOrEmpty(hintId));
        Assert.Equal(Localizer["FilterLens_SaveAsGroup_DisabledTitle"].Value, cut.Find($"#{hintId}").TextContent.Trim());
    }

    [Fact]
    public async Task SaveAsGroupClear_AllProperty_RestoresFocusToFilterPaneThroughGuard()
    {
        _source.Lenses.Returns(ImmutableList.Create(Summary("a"), Summary("b")));
        StubSaveAsGroupPrompt(PromptChoice.Secondary, "Group");
        var focusModule = JSInterop.SetupModule("./_content/EventLogExpert.UI/Common/focusGuard.js");

        var cut = Render<LensBreadcrumb>();
        await SaveActionButton(cut, Localizer["FilterLens_SaveAsGroup"].Value).ClickAsync(new MouseEventArgs());
        _commands.Received(1).SaveLensesAsGroup("Group", clearAfterSave: true);

        // Deferred clear lands: every contributing (property) lens is gone, the region unmounts, restore is GUARDED.
        _source.Lenses.Returns(ImmutableList<FilterLensSummary>.Empty);
        _source.Changed += Raise.Event<Action>();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".lens-breadcrumb")));
        AssertFilterPaneRestore(focusModule);
    }

    [Fact]
    public async Task SaveAsGroupClear_MixedStack_SurvivingTimeWindow_FiresGuardedRestore_RegionSurvives()
    {
        var property = Summary("prop");
        var time = TimeSummary();
        _source.Lenses.Returns(ImmutableList.Create(property, time));
        StubSaveAsGroupPrompt(PromptChoice.Secondary, "Group");
        var focusModule = JSInterop.SetupModule("./_content/EventLogExpert.UI/Common/focusGuard.js");

        var cut = Render<LensBreadcrumb>();
        await SaveActionButton(cut, Localizer["FilterLens_SaveAsGroup"].Value).ClickAsync(new MouseEventArgs());

        // Save-and-clear clears only the contributing property lens; the surviving time-window lens keeps the region
        // mounted, so the restore fires but GUARDED (the runtime decline is pinned in focusGuard.test.js).
        _source.Lenses.Returns(ImmutableList.Create(time));
        _source.Changed += Raise.Event<Action>();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".lens-chip")));
        AssertFilterPaneRestore(focusModule);
    }

    [Fact]
    public async Task SaveAsGroupClear_OverlappingClears_RestoreOncePerDisappearance()
    {
        var a = Summary("a");
        _source.Lenses.Returns(ImmutableList.Create(a));
        StubSaveAsGroupPrompt(PromptChoice.Secondary, "G1");
        var focusModule = JSInterop.SetupModule("./_content/EventLogExpert.UI/Common/focusGuard.js");

        var cut = Render<LensBreadcrumb>();
        await SaveActionButton(cut, Localizer["FilterLens_SaveAsGroup"].Value).ClickAsync(new MouseEventArgs());

        // A second property lens appears while save #1 persists; a second save-and-clear unions it into the pending set.
        var b = Summary("b");
        _source.Lenses.Returns(ImmutableList.Create(a, b));
        _source.Changed += Raise.Event<Action>();
        StubSaveAsGroupPrompt(PromptChoice.Secondary, "G2");
        await SaveActionButton(cut, Localizer["FilterLens_SaveAsGroup"].Value).ClickAsync(new MouseEventArgs());

        // Save #1 clears a; b is retained in the pending set across #1's completion. Save #2 then clears b.
        _source.Lenses.Returns(ImmutableList.Create(b));
        _source.Changed += Raise.Event<Action>();
        _source.Lenses.Returns(ImmutableList<FilterLensSummary>.Empty);
        _source.Changed += Raise.Event<Action>();

        // One guarded restore per disappearance - neither clear's unmount strands focus, and the second is not clobbered.
        cut.WaitForAssertion(() => Assert.Equal(2, focusModule.Invocations["focusSelectorIfNotElsewhere"].Count));
    }

    [Fact]
    public void WithLens_RendersLabel_AndRemoveButtonDispatchesRemoveLens()
    {
        var lens = Summary("abc");
        _source.Lenses.Returns(ImmutableList.Create(lens));

        var cut = Render<LensBreadcrumb>();

        Assert.Contains("Activity ID = abc", cut.Markup);

        cut.Find(".lens-chip-remove").Click();

        _commands.Received(1).RemoveLens(lens.Id);
    }

    private static void AssertFilterPaneRestore(BunitJSModuleInterop focusModule)
    {
        var invocation = Assert.Single(focusModule.Invocations["focusSelectorIfNotElsewhere"]);
        Assert.Equal(FilterPaneSelector, invocation.Arguments[0]);
    }

    private static IElement SaveActionButton(IRenderedComponent<LensBreadcrumb> cut, string text) =>
        cut.FindAll(".lens-action").Single(button => button.TextContent.Trim() == text);

    private static FilterLensSummary Summary(string value) =>
        new(FilterLensId.Create(), new FilterLensLabel.PropertyComparison(EventProperty.ActivityId, IsEqual: true, value));

    private static FilterLensSummary TimeSummary() =>
        new(FilterLensId.Create(), new FilterLensLabel.TimeWindow(DateTime.Now, TimeSpan.FromHours(1)), LensKind.TimeWindow);

    private void AssertFocusRestoredTo(IRenderedComponent<LensBreadcrumb> cut, FilterLensId expectedLensId) =>
        AssertFocusRestoredToChipButton(cut, expectedLensId, "_removeRefs");

    private void AssertFocusRestoredToChipButton(
        IRenderedComponent<LensBreadcrumb> cut, FilterLensId expectedLensId, string refsField)
    {
        var focusCall = Assert.Single(JSInterop.Invocations, invocation =>
            invocation.Identifier.Contains("domWrapper.focus", StringComparison.OrdinalIgnoreCase));
        var focusedId = ((ElementReference)focusCall.Arguments[0]!).Id;

        var field = typeof(LensBreadcrumb).GetField(refsField, BindingFlags.NonPublic | BindingFlags.Instance);
        var refs = (Dictionary<FilterLensId, ElementReference>)field!.GetValue(cut.Instance)!;

        Assert.True(refs.TryGetValue(expectedLensId, out var expectedRef));
        Assert.False(string.IsNullOrEmpty(focusedId));
        Assert.Equal(expectedRef.Id, focusedId);
    }

    private void StubSaveAsGroupPrompt(PromptChoice choice, string value) =>
        _alertDialog.DisplayPromptWithSecondary(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Func<string, string?>?>())
            .Returns(new PromptOutcome(choice, value));
}
