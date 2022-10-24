// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Library.Models;

/// <summary>
///     Represents a message that was originally stored in a Message File (see
///     https://msdn.microsoft.com/en-us/library/windows/desktop/aa363669(v=vs.85).aspx
///     for more about Message Files). This could be an Event, a Task (category),
///     or something else.
/// </summary>
public class MessageModel
{
    /// <summary>
    ///     The log name that this event will appear in.
    /// </summary>
    public string LogLink { get; set; }

    /// <summary>
    ///     The provider name for this message
    /// </summary>
    public string ProviderName { get; set; }

    /// <summary>
    ///     For raw ID format, see https://msdn.microsoft.com/en-us/library/windows/desktop/aa363651(v=vs.85).aspx
    /// </summary>
    public long RawId { get; set; }

    /// <summary>
    ///     The short ID - only the two low bytes of the raw ID
    /// </summary>
    public short ShortId { get; set; }

    /// <summary>
    ///     Arbitrary tag that can be set by the caller. We use this
    ///     to distinguish between the same messages retrieved from
    ///     different computers (for example, from different versions
    ///     of Windows or applications such as Exchange Server).
    /// </summary>
    public string Tag { get; set; }

    /// <summary>
    ///     Some providers may include an XML template that describes
    ///     the included properties.
    /// </summary>
    public string Template { get; set; }

    /// <summary>
    ///     The text of the message
    /// </summary>
    public string Text { get; set; }
}
