// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.AppTitle;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.Runtime.Update;
using EventLogExpert.UI.Common.Interop;
using EventLogExpert.UI.Keyboard;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Runtime.ExceptionServices;

namespace EventLogExpert.UI.Layout;

public sealed partial class MainLayout : IAsyncDisposable
{
    private readonly ModuleLoadSlot _focusRingSlot = new();
    private readonly ModuleLoadSlot _focusTooltipSlot = new();
    private readonly ModuleLoadSlot _themeSlot = new();

    private bool _disposed;
    private KeyboardShortcutLease? _keyboardLease;

    private enum ModuleLoadOutcome
    {
        Module,
        TeardownStarted,
        ExpectedFailure
    }

    [Inject] private IAppTitleService AppTitleService { get; init; } = null!;

    [Inject] private ICriticalErrorService CriticalErrorService { get; init; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private KeyboardShortcutService KeyboardShortcutService { get; init; } = null!;

    [Inject] private ISettingsService Settings { get; init; } = null!;

    [Inject] private IUpdateService UpdateService { get; init; } = null!;

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        Settings.ThemeChanged -= OnThemeChanged;

        IJSObjectReference? themeModule = _themeSlot.BeginTeardown();
        IJSObjectReference? focusRingModule = _focusRingSlot.BeginTeardown();
        IJSObjectReference? focusTooltipModule = _focusTooltipSlot.BeginTeardown();

        List<Exception> cleanupFailures = [];

        var keyboardLease = _keyboardLease;

        if (keyboardLease is not null)
        {
            await RunCleanupAndCaptureFailuresAsync(
                () => KeyboardShortcutService.UnregisterAsync(keyboardLease),
                cleanupFailures);
        }

        await RunCleanupAndCaptureFailuresAsync(
            () => JsModuleInterop.DisposeModuleStrictAsync(themeModule),
            cleanupFailures);

        await RunCleanupAndCaptureFailuresAsync(
            () => JsModuleInterop.DisposeModuleStrictAsync(focusRingModule),
            cleanupFailures);

        await RunCleanupAndCaptureFailuresAsync(
            () => JsModuleInterop.DisposeModuleStrictAsync(focusTooltipModule),
            cleanupFailures);

        if (cleanupFailures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(cleanupFailures[0]).Throw();
        }
        else if (cleanupFailures.Count > 1)
        {
            throw new AggregateException(cleanupFailures);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await RunFirstRenderStepsAsync();
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override async Task OnInitializedAsync()
    {
        _keyboardLease = KeyboardShortcutService.CreateLease();
        Settings.ThemeChanged += OnThemeChanged;

        await UpdateService.CheckForUpdates(Settings.IsPreReleaseEnabled);

        if (!_disposed)
        {
            AppTitleService.SetLogName(null);
        }

        await base.OnInitializedAsync();
    }

    private static bool IsExpectedJavaScriptFailure(Exception exception) =>
        exception is JSDisconnectedException or JSException or ObjectDisposedException or OperationCanceledException;

    private static async ValueTask RunCleanupAndCaptureFailuresAsync(
        Func<ValueTask> cleanup,
        List<Exception> cleanupFailures)
    {
        try
        {
            await cleanup();
        }
        catch (JSDisconnectedException) { }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception unexpected)
        {
            cleanupFailures.Add(unexpected);
        }
    }

    private Task<bool> ApplyThemeAsync() =>
        InvokeModuleCallAsync(
            _themeSlot,
            "./_content/EventLogExpert.UI/Layout/MainLayout.razor.js",
            "setTheme",
            Settings.Theme.ToString().ToLowerInvariant());

    private async Task ApplyThemeFromChangeAsync()
    {
        try
        {
            await ApplyThemeAsync();
        }
        catch (Exception exception)
        {
            if (_disposed)
            {
                CriticalErrorService.ReportCritical(exception);
            }
            else
            {
                await DispatchExceptionAsync(exception);
            }
        }
    }

    private async ValueTask DisposeLateModuleAsync(IJSObjectReference module)
    {
        try
        {
            await JsModuleInterop.DisposeModuleStrictAsync(module);
        }
        catch (Exception exception)
        {
            CriticalErrorService.ReportCritical(exception);
        }
    }

    private async Task<bool> InvokeModuleCallAsync(
        ModuleLoadSlot slot,
        string modulePath,
        string identifier,
        params object?[] arguments)
    {
        if (_disposed) { return false; }

        (ModuleLoadOutcome outcome, IJSObjectReference? module) = await LoadModuleAsync(slot, modulePath);

        if (outcome == ModuleLoadOutcome.TeardownStarted || _disposed) { return false; }

        if (module is null) { return true; }

        try
        {
            await module.InvokeVoidAsync(identifier, arguments);
        }
        catch (Exception exception) when (IsExpectedJavaScriptFailure(exception)) { }
        catch (Exception exception) when (_disposed)
        {
            CriticalErrorService.ReportCritical(exception);

            return false;
        }

        return !_disposed;
    }

    private async Task<(ModuleLoadOutcome Outcome, IJSObjectReference? Module)> LoadModuleAsync(
        ModuleLoadSlot slot,
        string modulePath)
    {
        ModuleImportAttempt attempt;

        lock (slot.Lock)
        {
            if (slot.TeardownStarted) { return (ModuleLoadOutcome.TeardownStarted, null); }

            attempt = slot.Attempt ??= new ModuleImportAttempt(StartImport(modulePath));
        }

        IJSObjectReference module;

        try
        {
            module = await attempt.Import;
        }
        catch (Exception exception) when (IsExpectedJavaScriptFailure(exception))
        {
            lock (slot.Lock)
            {
                if (ReferenceEquals(slot.Attempt, attempt))
                {
                    slot.Attempt = null;
                }

                return (slot.TeardownStarted ? ModuleLoadOutcome.TeardownStarted : ModuleLoadOutcome.ExpectedFailure,
                    null);
            }
        }
        catch (Exception exception)
        {
            bool claimedFailure;

            lock (slot.Lock)
            {
                claimedFailure = !attempt.FailureClaimed;
                attempt.FailureClaimed = true;
            }

            if (!claimedFailure) { return (ModuleLoadOutcome.ExpectedFailure, null); }

            if (_disposed)
            {
                CriticalErrorService.ReportCritical(exception);

                return (ModuleLoadOutcome.TeardownStarted, null);
            }

            throw;
        }

        IJSObjectReference? lateModule = null;
        ModuleLoadOutcome outcome;

        lock (slot.Lock)
        {
            if (slot.TeardownStarted)
            {
                outcome = ModuleLoadOutcome.TeardownStarted;

                if (!attempt.Arrived)
                {
                    lateModule = module;
                }
            }
            else
            {
                outcome = ModuleLoadOutcome.Module;
                attempt.Module = module;
            }

            attempt.Arrived = true;
        }

        if (lateModule is not null)
        {
            await DisposeLateModuleAsync(lateModule);
        }

        return (outcome, outcome == ModuleLoadOutcome.Module ? module : null);
    }

    private void OnThemeChanged() => _ = InvokeAsync(ApplyThemeFromChangeAsync);

    private async Task<bool> RegisterKeyboardShortcutsAsync()
    {
        if (_disposed) { return false; }

        try
        {
            await KeyboardShortcutService.EnsureRegisteredAsync(_keyboardLease!, JSRuntime);
        }
        catch (Exception exception) when (_disposed)
        {
            CriticalErrorService.ReportCritical(exception);

            return false;
        }

        return !_disposed;
    }

    private async Task RunFirstRenderStepsAsync()
    {
        if (!await ApplyThemeAsync()) { return; }

        if (!await RegisterKeyboardShortcutsAsync()) { return; }

        if (!await InvokeModuleCallAsync(
                _focusRingSlot,
                "./_content/EventLogExpert.UI/Common/keyboardFocusRing.js",
                "registerKeyboardFocusRing"))
        {
            return;
        }

        await InvokeModuleCallAsync(
            _focusTooltipSlot,
            "./_content/EventLogExpert.UI/Common/focusTooltip.js",
            "registerFocusTooltip");
    }

    private Task<IJSObjectReference> StartImport(string modulePath)
    {
        try
        {
            return JsModuleInterop.ImportModuleAsync(JSRuntime, modulePath).AsTask();
        }
        catch (Exception exception)
        {
            return Task.FromException<IJSObjectReference>(exception);
        }
    }

    private sealed class ModuleImportAttempt(Task<IJSObjectReference> import)
    {
        public bool Arrived { get; set; }

        public bool FailureClaimed { get; set; }

        public Task<IJSObjectReference> Import { get; } = import;

        public IJSObjectReference? Module { get; set; }
    }

    private sealed class ModuleLoadSlot
    {
        public ModuleImportAttempt? Attempt { get; set; }

        public Lock Lock { get; } = new();

        public bool TeardownStarted { get; private set; }

        public IJSObjectReference? BeginTeardown()
        {
            lock (Lock)
            {
                TeardownStarted = true;

                IJSObjectReference? published = Attempt?.Module;

                Attempt?.Module = null;

                return published;
            }
        }
    }
}
