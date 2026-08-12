// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Common;
using EventLogExpert.UI.Dashboard;
using EventLogExpert.UI.FilterEditor;
using EventLogExpert.UI.FilterLenses;
using EventLogExpert.UI.FilterLibrary;
using EventLogExpert.UI.Layout;
using EventLogExpert.UI.LogTable;
using EventLogExpert.UI.Menu;
using EventLogExpert.UI.Modal;
using Fluxor;
using Fluxor.Blazor.Web.Components;
using System.Reflection;
using DetailsPaneComponent = EventLogExpert.UI.DetailsPane.DetailsPane;
using FilterPaneComponent = EventLogExpert.UI.FilterPane.FilterPane;
using HistogramPaneComponent = EventLogExpert.UI.LogTable.Histogram.HistogramPane;
using StatusBarComponent = EventLogExpert.UI.StatusBar.StatusBar;

namespace EventLogExpert.UI.Tests.Architecture;

public sealed class UIFluxorBoundaryTests
{
    private const BindingFlags DeclaredMembers = BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    // Every concrete modal, discovered by reflection so an omitted modal cannot silently escape the disposal-contract
    // check below (this is what regressed the DatabaseToolsModal coverage when the modal set was tracked by hand).
    private static readonly Type[] s_modalTypes = typeof(ModalBase<>).Assembly.GetTypes()
        .Where(type => type is { IsClass: true, IsAbstract: false } && InheritsModalBase(type))
        .OrderBy(type => type.Name)
        .ToArray();

    public static TheoryData<Type> ModalTypes => [.. s_modalTypes];

    public static TheoryData<Type> SourceSurfaceTypes =>
        [typeof(AppStateComponentBase), typeof(PresentationViewComponentBase), typeof(SourceSubscription), typeof(LensBreadcrumb), typeof(MainContent), typeof(MenuBar), typeof(DetailsPaneComponent), typeof(EmptyStateDashboard), typeof(LogTabBar), typeof(StatusBarComponent), typeof(HistogramPaneComponent), typeof(LogTablePane), typeof(FilterRow), typeof(LibrarySavedTabHeader), typeof(LibraryEntryRow), typeof(FilterLibraryModal), typeof(FilterPaneComponent)];

    [Fact]
    public void EveryModal_IsCoveredByTheBoundaryEnumeration() =>
        // Guards the reflection discovery against finding nothing and pins the known modal set; update the count
        // deliberately when a modal is added or removed (currently the seven built-in modals plus FilterLibraryModal).
        Assert.Equal(8, s_modalTypes.Length);

    [Theory]
    [MemberData(nameof(ModalTypes))]
    public void Modal_IsAsyncDisposableAndNotFluxorComponent(Type modal)
    {
        Assert.True(
            modal.IsAssignableTo(typeof(IAsyncDisposable)),
            $"{modal.Name} must be IAsyncDisposable so the renderer disposes it through ModalBase's owned contract.");
        Assert.False(
            modal.IsAssignableTo(typeof(FluxorComponent)),
            $"{modal.Name} must not inherit FluxorComponent - the base swap removed that coupling.");
    }

    [Theory]
    [MemberData(nameof(SourceSurfaceTypes))]
    public void SourceSurface_DeclaresNoFluxorStateMembers(Type sourceSurface)
    {
        Type[] memberTypes =
        [
            .. sourceSurface.GetFields(DeclaredMembers).Select(field => field.FieldType),
            .. sourceSurface.GetProperties(DeclaredMembers).Select(property => property.PropertyType),
        ];

        Assert.DoesNotContain(memberTypes, IsFluxorState);
    }

    [Theory]
    [MemberData(nameof(SourceSurfaceTypes))]
    public void SourceSurface_DoesNotImplementActionSubscriber(Type sourceSurface) =>
        Assert.DoesNotContain(typeof(IActionSubscriber), sourceSurface.GetInterfaces());

    [Theory]
    [MemberData(nameof(SourceSurfaceTypes))]
    public void SourceSurface_DoesNotInheritFluxorComponent(Type sourceSurface) =>
        Assert.False(
            sourceSurface.IsAssignableTo(typeof(FluxorComponent)),
            $"{sourceSurface.Name} must not inherit FluxorComponent - it would restore Fluxor's auto-render coupling.");

    private static bool InheritsModalBase(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(ModalBase<>)) { return true; }
        }

        return false;
    }

    private static bool IsFluxorState(Type type)
    {
        if (type == typeof(IActionSubscriber)) { return true; }

        if (!type.IsGenericType) { return false; }

        Type definition = type.GetGenericTypeDefinition();

        return definition == typeof(IState<>) || definition == typeof(IStateSelection<,>);
    }
}
