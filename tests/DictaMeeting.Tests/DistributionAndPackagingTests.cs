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
        Assert.Equal(5, version.Build);
        Assert.Equal(0, version.Revision);

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
        Assert.Contains("#define MyAppVersion \"1.5.5\"", content);
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
        // El script usa ZipFile de .NET (no Compress-Archive de PowerShell) para mayor velocidad
        Assert.Contains("ZipFile", content);
        Assert.Contains("portable.zip", content);
    }

    [Fact]
    public void LauncherSource_ContainsDiagnosticCapabilities()
    {
        // Verifica que el código fuente del launcher incluye las capacidades de diagnóstico
        // requeridas para diagnosticar bloqueos por WatchGuard/EDR en el equipo afectado.
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var current = new DirectoryInfo(baseDir);
        string? launcherPath = null;

        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "src", "DictaMeeting.Launcher", "Launcher.cs");
            if (File.Exists(candidate))
            {
                launcherPath = candidate;
                break;
            }
            current = current.Parent;
        }

        Assert.NotNull(launcherPath);
        var content = File.ReadAllText(launcherPath);

        // El launcher debe capturar y registrar el PID del proceso hijo
        Assert.Contains("child.Id", content);

        // El launcher debe detectar terminación inmediata (< umbral en ms)
        Assert.Contains("WaitForExit", content);
        Assert.Contains("ImmediateExitThresholdMs", content);

        // El launcher debe capturar Win32Exception para obtener NativeErrorCode
        Assert.Contains("Win32Exception", content);
        Assert.Contains("NativeErrorCode", content);

        // El launcher debe registrar metadatos del ejecutable destino
        Assert.Contains("LogTargetExeMetadata", content);

        // El launcher debe registrar en LOCALAPPDATA (no en directorio de instalación)
        Assert.Contains("LocalApplicationData", content);
        Assert.Contains("DictaMeeting", content);
        Assert.Contains("logs", content);

        // El launcher NO debe registrar datos sensibles
        Assert.DoesNotContain("apiKey", content.ToLower());
        Assert.DoesNotContain("transcript", content.ToLower());
        Assert.DoesNotContain("openrouter", content.ToLower());
    }

    [Fact]
    public void LauncherSource_LogsBootDiagToLocalAppData()
    {
        // Verifica que el ARCHIVO DE LOG se escribe en LOCALAPPDATA y no en el directorio de instalación,
        // que puede estar protegido contra escritura en entornos corporativos.
        // NOTA: BaseDirectory puede aparecer en el código para construir la ruta del ejecutable destino
        // (diagnóstico legítimo), pero nunca debe usarse como raíz de la ruta del log.
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var current = new DirectoryInfo(baseDir);
        string? launcherPath = null;

        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "src", "DictaMeeting.Launcher", "Launcher.cs");
            if (File.Exists(candidate))
            {
                launcherPath = candidate;
                break;
            }
            current = current.Parent;
        }

        Assert.NotNull(launcherPath);
        var content = File.ReadAllText(launcherPath);

        // La función de log debe usar SpecialFolder.LocalApplicationData como raíz
        Assert.Contains("LocalApplicationData", content);
        // El directorio de log debe ser DictaMeeting/logs dentro de LOCALAPPDATA
        Assert.Contains("\"DictaMeeting\", \"logs\"", content);
        // El nombre del archivo de log debe ser boot.log
        Assert.Contains("boot.log", content);
    }

    [Fact]
    public void LauncherSource_PreservesUseShellExecuteTrue()
    {
        // UseShellExecute=true es requerido para que WPF arranque correctamente.
        // Verificamos que el diagnóstico mejorado no ha cambiado este comportamiento.
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var current = new DirectoryInfo(baseDir);
        string? launcherPath = null;

        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "src", "DictaMeeting.Launcher", "Launcher.cs");
            if (File.Exists(candidate))
            {
                launcherPath = candidate;
                break;
            }
            current = current.Parent;
        }

        Assert.NotNull(launcherPath);
        var content = File.ReadAllText(launcherPath);

        Assert.Contains("UseShellExecute = true", content);
    }
}
