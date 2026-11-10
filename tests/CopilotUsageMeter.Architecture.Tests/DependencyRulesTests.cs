using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class DependencyRulesTests
{
    private const string Prefix = "CopilotUsageMeter.";
    private const string SolutionFileName = "CopilotUsageMeter.slnx";

    private static readonly IReadOnlyDictionary<string, string[]> AllowedReferences =
        new Dictionary<string, string[]>
        {
            ["Contracts"] = [],
            ["Core"] = ["Contracts"],
            ["Readers.ChatHistoryCli"] = ["Core", "Contracts"],
            ["Readers.GitHub"] = ["Core", "Contracts"],
            ["Signals.FileWatcher"] = ["Core"],
            ["Transport.Pipes"] = ["Contracts"],
            ["Client"] = ["Core", "Contracts", "Transport.Pipes"],
            ["Panel.Core"] = ["Client", "Contracts"],
            ["Tray"] = ["Core", "Contracts", "Readers.ChatHistoryCli", "Readers.GitHub",
                "Signals.FileWatcher", "Transport.Pipes", "Client", "Panel.Core"],
            ["Panel.Vsix"] = ["Client", "Contracts", "Panel.Core"]
        };

    [Fact]
    public void AllProductionProjectsAreRegisteredInTheSolution()
    {
        var root = FindRepositoryRoot();
        var solutionProjects = XDocument.Load(Path.Combine(root, SolutionFileName))
            .Descendants("Project")
            .Select(project => (string?)project.Attribute("Path")
                ?? throw new InvalidDataException("Solution Project requires Path."))
            .Select(path => Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar))))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var projects = Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories);

        Assert.Equal(AllowedReferences.Count, projects.Length);
        Assert.Equal(AllowedReferences.Count + 1, solutionProjects.Count);
        Assert.False(File.Exists(Path.Combine(root, "CopilotUsageMeter.sln")));
        Assert.Contains(Path.Combine(root, "tests", "CopilotUsageMeter.Architecture.Tests",
            "CopilotUsageMeter.Architecture.Tests.csproj"), solutionProjects);
        foreach (var project in projects)
        {
            Assert.Contains(project, solutionProjects);
        }
    }

    [Fact]
    public void ProjectReferencesRespectLayerBoundaries()
    {
        foreach (var (name, _) in AllowedReferences)
        {
            var project = ReadProject(name);
            var references = project.Descendants("ProjectReference")
                .Select(reference => Path.GetFileNameWithoutExtension((string?)reference.Attribute("Include")
                    ?? throw new InvalidDataException("ProjectReference requires Include.")))
                .Select(reference => reference.StartsWith(Prefix, StringComparison.Ordinal)
                    ? reference[Prefix.Length..] : reference);

            Assert.Empty(FindForbiddenReferences(name, references));
        }
    }

    [Theory]
    [InlineData("Core", "Transport.Pipes")]
    [InlineData("Contracts", "Core")]
    [InlineData("Panel.Core", "Tray")]
    [InlineData("Readers.ChatHistoryCli", "Panel.Vsix")]
    [InlineData("Readers.GitHub", "Transport.Pipes")]
    [InlineData("Core", "VsChatHistoryToolkit")]
    public void DependencyGuardRejectsForbiddenReferences(string project, string reference)
    {
        Assert.Equal(new[] { reference }, FindForbiddenReferences(project, new[] { reference }));
    }

    [Theory]
    [InlineData("Core", "Contracts")]
    [InlineData("Client", "Transport.Pipes")]
    [InlineData("Panel.Vsix", "Panel.Core")]
    public void DependencyGuardAcceptsAllowedReferences(string project, string reference)
    {
        Assert.Empty(FindForbiddenReferences(project, new[] { reference }));
    }

    [Fact]
    public void UiAndVisualStudioDependenciesStayInTheirShells()
    {
        foreach (var (name, _) in AllowedReferences)
        {
            var project = ReadProject(name);
            var isUiShell = name is "Tray" or "Panel.Vsix";
            if (!isUiShell)
            {
                Assert.DoesNotContain(project.Descendants(), element =>
                    element.Name.LocalName is "UseWPF" or "UseWindowsForms"
                    && string.Equals(element.Value, "true", StringComparison.OrdinalIgnoreCase));
            }

            if (name != "Tray")
            {
                Assert.DoesNotContain(project.Descendants("UseWindowsForms"), element =>
                    string.Equals(element.Value, "true", StringComparison.OrdinalIgnoreCase));
            }

            var dependencies = project.Descendants().Where(element =>
                    element.Name.LocalName is "PackageReference" or "Reference")
                .Select(element => (string?)element.Attribute("Include") ?? string.Empty).ToArray();

            Assert.DoesNotContain(dependencies, dependency =>
                dependency.Contains("VsChatHistoryToolkit", StringComparison.OrdinalIgnoreCase));

            if (name != "Panel.Vsix")
            {
                Assert.DoesNotContain(dependencies, dependency =>
                    dependency.StartsWith("Microsoft.VisualStudio", StringComparison.OrdinalIgnoreCase)
                    || dependency.StartsWith("Microsoft.VSSDK", StringComparison.OrdinalIgnoreCase)
                    || dependency.StartsWith("EnvDTE", StringComparison.OrdinalIgnoreCase));
            }

            if (name != "Tray")
            {
                Assert.DoesNotContain(dependencies, dependency =>
                    dependency.StartsWith("System.Windows.Forms", StringComparison.OrdinalIgnoreCase));
            }

            if (!isUiShell)
            {
                Assert.DoesNotContain(dependencies, dependency =>
                    dependency.StartsWith("Presentation", StringComparison.OrdinalIgnoreCase)
                    || dependency.StartsWith("WindowsBase", StringComparison.OrdinalIgnoreCase)
                    || dependency.StartsWith("System.Windows", StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    [Theory]
    [InlineData("Contracts")]
    [InlineData("Core")]
    [InlineData("Panel.Core")]
    public void CompiledSharedLogicHasNoInfrastructureTypes(string project)
    {
        var assembly = Assembly.Load(new AssemblyName(Prefix + project));
        using var stream = File.OpenRead(assembly.Location);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var types = metadata.TypeReferences.Select(handle =>
        {
            var type = metadata.GetTypeReference(handle);
            return metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
        });

        Assert.DoesNotContain(types, IsForbiddenInfrastructureType);
    }

    [Theory]
    [InlineData("System.Diagnostics.Process")]
    [InlineData("System.Diagnostics.ProcessStartInfo")]
    [InlineData("System.IO.FileSystemWatcher")]
    [InlineData("System.IO.Pipes.NamedPipeServerStream")]
    [InlineData("System.Windows.Controls.UserControl")]
    [InlineData("Microsoft.VisualStudio.Shell.AsyncPackage")]
    [InlineData("EnvDTE.DTE")]
    public void TypeGuardRejectsInfrastructureTypes(string type)
    {
        Assert.True(IsForbiddenInfrastructureType(type));
    }

    [Fact]
    public void TypeGuardAcceptsPlainFrameworkTypes()
    {
        Assert.False(IsForbiddenInfrastructureType("System.DateTimeOffset"));
    }

    private static bool IsForbiddenInfrastructureType(string type) =>
        type.StartsWith("System.Windows.", StringComparison.Ordinal)
        || type.StartsWith("Microsoft.VisualStudio.", StringComparison.Ordinal)
        || type.StartsWith("EnvDTE.", StringComparison.Ordinal)
        || type.StartsWith("System.IO.Pipes.", StringComparison.Ordinal)
        || type is "System.Diagnostics.Process" or "System.Diagnostics.ProcessStartInfo"
            or "System.IO.FileSystemWatcher";

    private static string[] FindForbiddenReferences(string project, IEnumerable<string> references) =>
        references.Where(reference => !AllowedReferences[project].Contains(reference, StringComparer.Ordinal)).ToArray();

    private static XDocument ReadProject(string name)
    {
        var project = Prefix + name;
        return XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", project, project + ".csproj"));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException($"Cannot locate {SolutionFileName} above the test output.");
    }
}