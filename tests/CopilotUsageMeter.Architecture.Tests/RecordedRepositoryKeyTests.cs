using CopilotUsageMeter.Panel.Core;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class RecordedRepositoryKeyTests
{
    [Theory]
    [InlineData("https://github.com/001/002.git", "001/002")]
    [InlineData("git@github.com:001/002.git", "001/002")]
    [InlineData("ssh://git@github.com/001/002", "001/002")]
    [InlineData("https://github.com/001/002/003", null)]
    [InlineData("https://000.example/001/002", null)]
    [InlineData("C:\\001\\002", null)]
    [InlineData("https://github.com/001/002?003", null)]
    [InlineData(null, null)]
    public void ReturnsOnlySupportedPublicOriginKey(string? origin, string? expected) =>
        Assert.Equal(expected, RecordedRepositoryKey.FromOrigin(origin));
}