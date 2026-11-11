using System.Xml.Linq;
using CopilotUsageMeter.Panel.Core;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class ProductVersionTests
{
    [Theory]
    [InlineData("0.1.15", "0.1.15")]
    [InlineData("0.1.15.0", "0.1.15")]
    [InlineData("0.1.15+abc123", "0.1.15")]
    [InlineData("0.1.15-beta.1+abc", "0.1.15")]
    [InlineData("1.2.3.4", "1.2.3.4")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void FormatKeepsOnlyTheReleaseNumber(string? raw, string expected) =>
        Assert.Equal(expected, ProductVersion.Format(raw));

    [Fact]
    public void ManifestVersionMatchesTheSharedProductVersion()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "CopilotUsageMeter.slnx"))) root = Path.GetDirectoryName(root) ?? throw new DirectoryNotFoundException("Repository root not found.");
        var props = XDocument.Load(Path.Combine(root, "Directory.Build.props")).Descendants("Version").Single().Value;
        var manifest = XDocument.Load(Path.Combine(root, "src", "CopilotUsageMeter.Panel.Vsix", "source.extension.vsixmanifest"))
            .Descendants().First(e => e.Name.LocalName == "Identity").Attribute("Version")!.Value;
        Assert.Equal(manifest, props);
    }
}
