// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Interop;

internal enum EvtEventMetadataPropertyId
{
    ID,
    Version,
    Channel,
    Level,
    Opcode,
    Task,
    Keyword,
    MessageID,
    Template
}

internal enum EvtFormatMessageFlags
{
    Event = 1,
    Level,
    Task,
    Opcode,
    Keyword,
    Channel,
    Provider,
    Id,
    Xml
}

internal enum EvtLogPropertyId
{
    CreationTime,
    LastAccessTime,
    LastWriteTime,
    FileSize,
    Attributes,
    NumberOfLogRecords,
    OldestRecordNumber,
    Full,
}

internal enum EvtPublisherMetadataPropertyId
{
    PublisherGuid,
    ResourceFilePath,
    ParameterFilePath,
    MessageFilePath,
    HelpLink,
    PublisherMessageID,
    ChannelReferences,
    ChannelReferencePath,
    ChannelReferenceIndex,
    ChannelReferenceID,
    ChannelReferenceFlags,
    ChannelReferenceMessageID,
    Levels,
    LevelName,
    LevelValue,
    LevelMessageID,
    Tasks,
    TaskName,
    TaskEventGuid,
    TaskValue,
    TaskMessageID,
    Opcodes,
    OpcodeName,
    OpcodeValue,
    OpcodeMessageID,
    Keywords,
    KeywordName,
    KeywordValue,
    KeywordMessageID
}

internal enum EvtRenderContextFlags
{
    Values,
    System,
    User
}

/// <summary>Defines the values that specify what to render</summary>
internal enum EvtRenderFlags
{
    /// <summary>Render the <see cref="EvtVariant" /> properties specified in the rendering context</summary>
    EventValues,
    /// <summary>Render the event as an XML string</summary>
    EventXml,
    /// <summary>Render the bookmark as an XML string, so that you can easily persist the bookmark for use later</summary>
    Bookmark
}

[Flags]
internal enum EvtSubscribeFlags
{
    ToFutureEvents = 1,
    StartAtOldestRecord = 2,
    StartAfterBookmark = 3,
#pragma warning disable CA1069 // Enums values should not be duplicated
    OriginMask = 3,
#pragma warning restore CA1069 // Enums values should not be duplicated
    TolerateQueryErrors = 0x1000,
    Strict = 0x10000
}

internal enum EvtSystemPropertyId
{
    ProviderName,
    ProviderGuid,
    EventId,
    Qualifiers,
    Level,
    Task,
    Opcode,
    Keywords,
    TimeCreated,
    EventRecordId,
    ActivityId,
    RelatedActivityID,
    ProcessID,
    ThreadID,
    Channel,
    Computer,
    UserID,
    Version
}

internal enum EvtChannelConfigPropertyId
{
    EvtChannelConfigEnabled = 0,
    EvtChannelConfigIsolation = 1,
    EvtChannelConfigType = 2,
    EvtChannelConfigOwningPublisher = 3,
}

internal enum EvtVariantType
{
    Null,
    String,
    AnsiString,
    SByte,
    Byte,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Single,
    Double,
    Boolean,
    Binary,
    Guid,
    SizeT,
    FileTime,
    SysTime,
    Sid,
    HexInt32,
    HexInt64,
    Handle,
    Xml,

    // Array types (base type | EVT_VARIANT_TYPE_ARRAY)
    StringArray = 129,
    ByteArray = 132,
    UInt16Array = 134,
    UInt32Array = 136,
    HexInt32Array = 148
}

[Flags]
internal enum SeekFlags
{
    RelativeToFirst = 1,
    RelativeToLast = 2,
    RelativeToCurrent = 3,
    RelativeToBookmark = 4,
    OriginMask = 7,
    Strict = 65536
}
