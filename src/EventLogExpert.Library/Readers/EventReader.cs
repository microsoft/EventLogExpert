using System.Diagnostics.Eventing.Reader;

namespace EventLogExpert.Library.Readers;

/// <summary>
///     This class is called from NodeJS in order to read an event log.
///     NodeJS should call:
///     GetActiveEventLogReader - to read an active log like Application
///     GetEventLogFileReader - to read an evtx file
///     The returned delegate can be called repeatedly to read events
///     until all events have been read. By exporting a delegate to NodeJS,
///     we are able to expose the state of this class (and keep track of
///     our position in the log) even though NodeJS has no direct access to
///     the state.
/// </summary>
public class EventReader
{
    private const int BatchSize = 1000;

    /// <summary>
    ///     Note this method is synchronous and must be called synchronously
    ///     from NodeJS.
    /// </summary>
    /// <param name="logName"></param>
    /// <returns>A delegate that must be called asynchronously from NodeJS</returns>
    public static Func<Task<List<EventRecord>>> GetActiveEventLogReader(string logName)
    {
        var reader = new EventLogReader(logName, PathType.LogName);
        var readComplete = false;

        // The delegate returned is async
        return async () =>
        {
            if (readComplete)
            {
                return null;
            }

            return await Task<List<EventRecord>>.Factory.StartNew(() =>
            {
                var count = 0;
                var events = new List<EventRecord>();
                EventRecord evt;

                while (count < BatchSize && null != (evt = reader.ReadEvent()))
                {
                    count++;

                    events.Add(evt);
                }

                if (count < 1)
                {
                    readComplete = true;
                    reader.Dispose();
                    return null;
                }

                if (count < BatchSize)
                {
                    readComplete = true;
                    //reader.Dispose();
                }

                return events;
            });
        };
    }

    /// <summary>
    ///     Note this method is synchronous and must be called synchronously
    ///     from NodeJS.
    /// </summary>
    /// <param name="file"></param>
    /// <returns>A delegate that must be called asynchronously from NodeJS</returns>
    public static Func<Task<List<EventRecord>>> GetEventLogFileReader(string file)
    {
        var reader = new EventLogReader(file, PathType.FilePath);
        var readComplete = false;

        // The delegate returned is async
        return async () =>
        {
            if (readComplete)
            {
                return null;
            }

            return await Task<List<EventRecord>>.Factory.StartNew(() =>
            {
                var count = 0;
                var events = new List<EventRecord>();
                EventRecord evt;

                while (count < BatchSize && null != (evt = reader.ReadEvent()))
                {
                    count++;
                    events.Add(evt);
                }

                if (count < 1)
                {
                    readComplete = true;
                    reader.Dispose();
                    return null;
                }

                if (count < BatchSize)
                {
                    readComplete = true;
                    reader.Dispose();
                }

                return events;
            });
        };
    }
}