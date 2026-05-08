// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventProviderDatabase;
using EventLogExpert.Eventing.Models;

namespace EventLogExpert.Eventing.Tests.EventProviderDatabase;

public sealed class ProviderJsonContextTests
{
    public static TheoryData<Type> SourceGeneratedTypes => new()
    {
        typeof(MessageModel),
        typeof(EventModel),
        typeof(IReadOnlyList<MessageModel>),
        typeof(IEnumerable<MessageModel>),
        typeof(IReadOnlyList<EventModel>),
        typeof(IDictionary<long, string>),
        typeof(IDictionary<int, string>),
        typeof(List<MessageModel>),
        typeof(List<EventModel>),
        typeof(Dictionary<long, string>),
        typeof(Dictionary<int, string>),
    };

    [Theory]
    [MemberData(nameof(SourceGeneratedTypes))]
    public void Default_ShouldProvideMetadataFor_ProviderDtoType(Type type)
    {
        var typeInfo = ProviderJsonContext.Default.GetTypeInfo(type);

        Assert.NotNull(typeInfo);
        Assert.Equal(type, typeInfo!.Type);
    }

    [Fact]
    public void RoundTrip_IReadOnlyListMessageModel_PreservesData()
    {
        var sample = new MessageModel
        {
            ProviderName = "TestProvider",
            RawId = 0x1234567890ABCDEF,
            ShortId = 0x1234,
            Tag = "tag",
            Template = "template",
            Text = "text",
            LogLink = "log"
        };

        IReadOnlyList<MessageModel> original = new List<MessageModel> { sample };

        var bytes = CompressedJsonValueConverter<IReadOnlyList<MessageModel>>.ConvertToCompressedJson(original);
        var restored = CompressedJsonValueConverter<IReadOnlyList<MessageModel>>.ConvertFromCompressedJson(bytes);

        Assert.NotNull(restored);
        Assert.Single(restored);
        Assert.Equal(sample.ProviderName, restored[0].ProviderName);
        Assert.Equal(sample.RawId, restored[0].RawId);
        Assert.Equal(sample.ShortId, restored[0].ShortId);
        Assert.Equal(sample.Tag, restored[0].Tag);
        Assert.Equal(sample.Template, restored[0].Template);
        Assert.Equal(sample.Text, restored[0].Text);
        Assert.Equal(sample.LogLink, restored[0].LogLink);
    }

    [Fact]
    public void RoundTrip_IDictionaryLongString_PreservesData()
    {
        IDictionary<long, string> original = new Dictionary<long, string>
        {
            [1L] = "one",
            [0x100000000L] = "big"
        };

        var bytes = CompressedJsonValueConverter<IDictionary<long, string>>.ConvertToCompressedJson(original);
        var restored = CompressedJsonValueConverter<IDictionary<long, string>>.ConvertFromCompressedJson(bytes);

        Assert.NotNull(restored);
        Assert.Equal(2, restored.Count);
        Assert.Equal("one", restored[1L]);
        Assert.Equal("big", restored[0x100000000L]);
    }
}
