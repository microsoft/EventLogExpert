// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Banner;

namespace EventLogExpert.Runtime.Tests.Banner;

public sealed class ExportProgressBannerServiceTests
{
    [Fact]
    public void Begin_NullCancel_Throws()
    {
        ExportProgressBannerService sut = new();

        Assert.Throws<ArgumentNullException>(() => sut.Begin(null!));
    }

    [Fact]
    public void Begin_RaisesStateChangedOnce()
    {
        ExportProgressBannerService sut = new();
        int fires = 0;
        sut.StateChanged += () => fires++;

        sut.Begin(() => { });

        Assert.Equal(1, fires);
    }

    [Fact]
    public void Begin_SetsCurrentExport()
    {
        ExportProgressBannerService sut = new();

        sut.Begin(() => { });

        ExportProgressEntry? current = sut.CurrentExport;
        Assert.NotNull(current);
    }

    [Fact]
    public void Begin_StateChangedHandler_ObservesCurrentExportAlreadySet()
    {
        // The entry is committed under the lock before StateChanged is raised, so a subscriber that
        // reads CurrentExport during the callback sees the new value (and does not deadlock).
        ExportProgressBannerService sut = new();
        ExportProgressEntry? observed = null;
        sut.StateChanged += () => observed = sut.CurrentExport;

        sut.Begin(() => { });

        Assert.NotNull(observed);
    }

    [Fact]
    public void CurrentExport_Cancel_InvokesProvidedDelegate()
    {
        ExportProgressBannerService sut = new();
        bool canceled = false;
        sut.Begin(() => canceled = true);

        ExportProgressEntry? current = sut.CurrentExport;
        Assert.NotNull(current);
        current.Cancel();

        Assert.True(canceled);
    }

    [Fact]
    public void End_ClearsCurrentExport()
    {
        ExportProgressBannerService sut = new();
        sut.Begin(() => { });

        sut.End();

        Assert.Null(sut.CurrentExport);
    }

    [Fact]
    public void End_RaisesStateChanged()
    {
        ExportProgressBannerService sut = new();
        sut.Begin(() => { });
        int fires = 0;
        sut.StateChanged += () => fires++;

        sut.End();

        Assert.Equal(1, fires);
    }

    [Fact]
    public void End_WhenNoExportInProgress_DoesNotRaiseStateChanged()
    {
        ExportProgressBannerService sut = new();
        int fires = 0;
        sut.StateChanged += () => fires++;

        sut.End();

        Assert.Equal(0, fires);
        Assert.Null(sut.CurrentExport);
    }
}
