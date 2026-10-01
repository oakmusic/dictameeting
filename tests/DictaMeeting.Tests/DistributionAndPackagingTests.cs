using System.Reflection;
using DictaMeeting.App.ViewModels;
using Xunit;

namespace DictaMeeting.Tests;

public class DistributionAndPackagingTests
{
    [Fact]
    public void AppAssembly_HasProperVersioningAndMetadata()
    {
        var assembly = typeof(MainViewModel).Assembly;
        var version = assembly.GetName().Version;

        Assert.NotNull(version);
        Assert.Equal(1, version.Major);
        Assert.Equal(5, version.Minor);

        var titleAttr = assembly.GetCustomAttribute<AssemblyTitleAttribute>();
        Assert.NotNull(titleAttr);
        Assert.Contains("DictaMeeting", titleAttr.Title);

        var productAttr = assembly.GetCustomAttribute<AssemblyProductAttribute>();
        Assert.NotNull(productAttr);
        Assert.Equal("DictaMeeting", productAttr.Product);
    }

    [Fact]
    public void InnoSetupScript_ExistsAndContainsAllRequiredSections()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        // Search upward for installer/DictaMeeting-Setup.iss
        var current = new DirectoryInfo(baseDir);
        string? issPath = null;

        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "installer", "DictaMeeting-Setup.iss");
            if (File.Exists(candidate))
            {
                issPath = candidate;
                break;
            }
            current = current.Parent;
        }

        Assert.NotNull(issPath);
        var content = File.ReadAllText(issPath);

        Assert.Contains("[Setup]", content);
        Assert.Contains("#define MyAppName \"DictaMeeting\"", content);
        Assert.Contains("#define MyAppVersion \"1.5.2\"", content);
        Assert.Contains("AppName={#MyAppName}", content);
        Assert.Contains("PrivilegesRequired=lowest", content);
        Assert.Contains("[Files]", content);
        Assert.Contains("[Dirs]", content);
        Assert.Contains("[Icons]", content);
        Assert.Contains("[Run]", content);
        Assert.Contains("DictaMeeting.exe", content);
    }

    [Fact]
    public void BuildDistributionScript_ExistsAndReferencesSelfContainedPublish()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var current = new DirectoryInfo(baseDir);
        string? scriptPath = null;

        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "scripts", "build-distribution.ps1");
            if (File.Exists(candidate))
            {
                scriptPath = candidate;
                break;
            }
            current = current.Parent;
        }

        Assert.NotNull(scriptPath);
        var content = File.ReadAllText(scriptPath);

        Assert.Contains("--self-contained true", content);
        Assert.Contains("-p:PublishReadyToRun=true", content);
        Assert.Contains("Compress-Archive", content);
        Assert.Contains("portable.zip", content);
    }
}
