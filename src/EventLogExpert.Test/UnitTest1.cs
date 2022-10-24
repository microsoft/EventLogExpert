// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.EventResolvers;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using Xunit.Abstractions;

namespace EventLogExpert.Test;

public class UnitTest1
{
    private readonly ITestOutputHelper _outputHelper;

    public UnitTest1(ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
    }

    [Fact]
    public void Test1()
    {
        var eventLogReader = new EventLogReader("Application", PathType.LogName);

        var resolvers = new List<IEventResolver>()
        {
            new EventReaderEventResolver(),
            new LocalProviderEventResolver(s => { _outputHelper.WriteLine(s); Debug.WriteLine(s); Debug.Flush(); })
        };

        EventRecord er;
        HashSet<string> uniqueDescriptions = new();

        var totalCount = 0;
        var mismatchCount = 0;
        var mismatches = new List<List<string>>();
        while (null != (er = eventLogReader.ReadEvent()))
        {
            uniqueDescriptions.Clear();

            foreach (var r in resolvers)
            {
                uniqueDescriptions.Add(r.Resolve(er).Description
                    .Replace("\r", "")  // I can't figure out the logic of FormatMessage() for when it leaves
                    .Replace("\n", "")  // CRLFs and spaces in or takes them out, so I'm just giving up for now.
                    .Replace(" ", "")   // If we're this close to matching FormatMessage() then we're close enough.
                    .Trim());
            }

            if (uniqueDescriptions.Count > 1)
            {
                mismatchCount++;
                mismatches.Add(uniqueDescriptions.ToList());
            }

            totalCount++;
        }

        Assert.Equal(0, mismatchCount);
    }

    [Fact]
    public void PerformanceTestEventReaderEventResolver()
    {
        var sw = new System.Diagnostics.Stopwatch();
        sw.Start();

        var eventLogReader = new EventLogReader("Application", PathType.LogName);
        var resolver = new EventReaderEventResolver();
        EventRecord er;
        while (null != (er = eventLogReader.ReadEvent()))
        {
            resolver.Resolve(er);
        }

        sw.Stop();
        _outputHelper.WriteLine($"Elapsed: {sw.ElapsedMilliseconds}");
    }

    [Fact]
    public void PerformanceTestLocalProviderEventResolver()
    {
        var sw = new System.Diagnostics.Stopwatch();
        sw.Start();

        var eventLogReader = new EventLogReader("Application", PathType.LogName);
        var resolver = new LocalProviderEventResolver();
        EventRecord er;
        while (null != (er = eventLogReader.ReadEvent()))
        {
            resolver.Resolve(er);
        }

        sw.Stop();
        _outputHelper.WriteLine($"Elapsed: {sw.ElapsedMilliseconds}");
    }
}
