// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Scenarios;

namespace EventLogExpert.Runtime.Tests.Scenarios;

public sealed class EvtxChannelReaderTests
{
    [Fact]
    public void ReadChannel_FileThatIsNotAnEvtx_ReturnsUnreadable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"eventlogexpert-notevtx-{Guid.NewGuid():N}.evtx");
        File.WriteAllText(path, "this is not an event log");

        try
        {
            var result = new EvtxChannelReader().ReadChannel(path);

            Assert.True(result.Failed);
            Assert.Null(result.Channel);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadChannel_MissingFile_ReturnsUnreadable()
    {
        var result = new EvtxChannelReader().ReadChannel(
            Path.Combine(Path.GetTempPath(), $"eventlogexpert-missing-{Guid.NewGuid():N}.evtx"));

        Assert.True(result.Failed);
        Assert.Null(result.Channel);
    }
}
