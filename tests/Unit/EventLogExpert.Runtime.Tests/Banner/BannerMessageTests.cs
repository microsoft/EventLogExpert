// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Banner;

namespace EventLogExpert.Runtime.Tests.Banner;

public sealed class BannerMessageTests
{
    [Fact]
    public void EmptyLogs_WhenDisplayNamesEmpty_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => new EmptyLogs([]));

        Assert.Equal("displayNames", ex.ParamName);
    }

    [Fact]
    public void EmptyLogs_WhenDisplayNamesNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new EmptyLogs(null!));
    }
}
