// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Database.Upgrade;
using EventLogExpert.UI.Database;
using EventLogExpert.UI.Tests.Localization;
using EventLogExpert.UI.Tests.TestUtils;
using System.Xml.Linq;

namespace EventLogExpert.UI.Tests.Database;

public sealed class UpgradePhaseLocalizerTests
{
    private readonly MarkerLocalizer _localizer = new();

    public static TheoryData<UpgradePhase, string> PhaseKeys()
    {
        TheoryData<UpgradePhase, string> data = new();

        foreach (UpgradePhase phase in Enum.GetValues<UpgradePhase>())
        {
            data.Add(phase, $"Db_UpgradePhase_{phase}");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PhaseKeys))]
    public void Describe_RoutesEveryUpgradePhaseToMemberNamedKey(UpgradePhase phase, string expectedKey) =>
        Assert.Equal($"[[{expectedKey}]]", UpgradePhaseLocalizer.Describe(_localizer, phase));

    [Fact]
    public void Describe_UnknownPhase_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => UpgradePhaseLocalizer.Describe(_localizer, (UpgradePhase)999));

    [Fact]
    public void NeutralPhaseValue_KeepMigratingSchemaByteIdentity() =>
        Assert.Equal("Migrating schema", NeutralValue("Db_UpgradePhase_MigratingSchema"));

    private static string NeutralValue(string key) =>
        XDocument.Load(LocalizationSourceScan.ResxPath)
            .Root!
            .Elements("data")
            .Single(element => (string?)element.Attribute("name") == key)
            .Element("value")!
            .Value;
}
