// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.RegularExpressions;

namespace EventLogExpert.UI.Tests.Modal;

public sealed class ModalHeightInvariantTests
{
    private static readonly Regex s_flexBodyLayoutRegex = new(
        @"\bBodyLayout\s*=\s*""@?ModalBodyLayout\.Flex""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex s_heightAttrRegex = new(
        @"\bHeight\s*=",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex s_modalChromeElementRegex = new(
        @"<ModalChrome\b[^>]*?>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    [Fact]
    public void EveryFlexBodyLayoutModal_DeclaresHeightAttribute()
    {
        var modalDir = LocateModalSourceDirectory();
        var modalFiles = Directory.EnumerateFiles(modalDir, "*Modal.razor", SearchOption.AllDirectories).ToList();
        Assert.NotEmpty(modalFiles);

        var violations = new List<string>();

        foreach (var path in modalFiles)
        {
            var content = File.ReadAllText(path);
            foreach (Match element in s_modalChromeElementRegex.Matches(content))
            {
                var attrs = element.Value;
                if (!s_flexBodyLayoutRegex.IsMatch(attrs)) { continue; }
                if (s_heightAttrRegex.IsMatch(attrs)) { continue; }
                violations.Add($"{Path.GetFileName(path)}: <ModalChrome BodyLayout=\"ModalBodyLayout.Flex\"> without Height= attribute");
            }
        }

        Assert.True(violations.Count == 0, "Modals with BodyLayout=Flex must declare Height=:\n" + string.Join("\n", violations));
    }

    private static string LocateModalSourceDirectory()
    {
        var candidate = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(candidate))
        {
            var solutionRoot = Path.Combine(candidate, "src", "EventLogExpert.UI");
            if (Directory.Exists(solutionRoot)) { return solutionRoot; }
            candidate = Path.GetDirectoryName(candidate);
        }
        throw new DirectoryNotFoundException("Could not locate src/EventLogExpert.UI from test base directory.");
    }
}
