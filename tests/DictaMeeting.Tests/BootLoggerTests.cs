using System;
using System.IO;
using DictaMeeting.App.Services;
using Xunit;

namespace DictaMeeting.Tests;

public class BootLoggerTests
{
    [Fact]
    public void LogBoot_CreatesDirectoryAndFile_WhenNotExisting()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DictaMeeting_Test_BootLog_" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.False(Directory.Exists(tempDir));

            BootLogger.LogBoot("Mensaje de prueba de arranque.", tempDir);

            Assert.True(Directory.Exists(tempDir));
            var logPath = Path.Combine(tempDir, "boot.log");
            Assert.True(File.Exists(logPath));

            var content = File.ReadAllText(logPath);
            Assert.Contains("Mensaje de prueba de arranque.", content);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void LogBoot_RotatesFile_WhenExceeding256KB()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DictaMeeting_Test_Rotate_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var logPath = Path.Combine(tempDir, "boot.log");

            // Crear un boot.log de tamaño mayor a 256 KB (257 KB)
            byte[] initialData = new byte[257 * 1024];
            Array.Fill(initialData, (byte)'A');
            File.WriteAllBytes(logPath, initialData);

            // Escribir un nuevo mensaje que disparará la rotación
            BootLogger.LogBoot("Línea tras rotación.", tempDir);

            var log1Path = Path.Combine(tempDir, "boot.1.log");
            Assert.True(File.Exists(log1Path), "boot.1.log debe existir tras rotar.");
            var fi1 = new FileInfo(log1Path);
            Assert.True(fi1.Length >= 256 * 1024, "boot.1.log debe contener los datos previos.");

            Assert.True(File.Exists(logPath), "boot.log debe crearse de nuevo para el nuevo registro.");
            var activeContent = File.ReadAllText(logPath);
            Assert.Contains("Línea tras rotación.", activeContent);
            Assert.True(new FileInfo(logPath).Length < 256 * 1024, "El nuevo boot.log debe ser pequeño.");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void LogBoot_MaintainsMaxTwoHistoricalFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DictaMeeting_Test_MaxHistory_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var logPath = Path.Combine(tempDir, "boot.log");
            var log1Path = Path.Combine(tempDir, "boot.1.log");
            var log2Path = Path.Combine(tempDir, "boot.2.log");
            var log3Path = Path.Combine(tempDir, "boot.3.log");

            // Ciclo 1: Llenar boot.log (> 256 KB)
            File.WriteAllText(logPath, new string('1', 260 * 1024));
            BootLogger.LogBoot("Entrada 1", tempDir);

            Assert.True(File.Exists(log1Path));
            Assert.False(File.Exists(log2Path));

            // Ciclo 2: Llenar boot.log (> 256 KB)
            File.WriteAllText(logPath, new string('2', 260 * 1024));
            BootLogger.LogBoot("Entrada 2", tempDir);

            Assert.True(File.Exists(log1Path));
            Assert.True(File.Exists(log2Path));

            // Ciclo 3: Llenar boot.log (> 256 KB)
            File.WriteAllText(logPath, new string('3', 260 * 1024));
            BootLogger.LogBoot("Entrada 3", tempDir);

            Assert.True(File.Exists(log1Path));
            Assert.True(File.Exists(log2Path));
            Assert.False(File.Exists(log3Path), "No debe crearse un tercer histórico boot.3.log.");

            // Verificar que no hay más de 3 archivos de log en total (boot.log, boot.1.log, boot.2.log)
            var allLogs = Directory.GetFiles(tempDir, "boot*.log");
            Assert.Equal(3, allLogs.Length);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void LogBoot_CleansUpLegacyBootOldLog()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DictaMeeting_Test_LegacyCleanup_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var logPath = Path.Combine(tempDir, "boot.log");
            var legacyOldPath = Path.Combine(tempDir, "boot.old.log");

            File.WriteAllText(legacyOldPath, "Log heredado de versiones anteriores");
            File.WriteAllText(logPath, new string('Z', 260 * 1024));

            BootLogger.LogBoot("Línea que dispara rotación y limpieza.", tempDir);

            Assert.False(File.Exists(legacyOldPath), "El archivo legacy boot.old.log debe haber sido eliminado.");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void LogBoot_DoesNotThrow_WhenFileIsLocked()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DictaMeeting_Test_Lock_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var logPath = Path.Combine(tempDir, "boot.log");
            File.WriteAllText(logPath, "Línea inicial existente.");

            // Bloquear el archivo de forma exclusiva simulando otro proceso o antivirus
            using (var lockStream = new FileStream(logPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                // Debe capturarse el error silenciosamente y no lanzar excepción
                var exception = Record.Exception(() =>
                {
                    BootLogger.LogBoot("Línea mientras el archivo está bloqueado.", tempDir);
                });

                Assert.Null(exception);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void LogBoot_DoesNotThrow_WhenFileIsReadOnly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DictaMeeting_Test_ReadOnly_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var logPath = Path.Combine(tempDir, "boot.log");
            File.WriteAllText(logPath, "Línea de solo lectura.");
            File.SetAttributes(logPath, FileAttributes.ReadOnly);

            var exception = Record.Exception(() =>
            {
                BootLogger.LogBoot("Línea en archivo solo lectura.", tempDir);
            });

            Assert.Null(exception);

            // Restaurar atributos para limpieza
            File.SetAttributes(logPath, FileAttributes.Normal);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    var logPath = Path.Combine(tempDir, "boot.log");
                    if (File.Exists(logPath))
                    {
                        File.SetAttributes(logPath, FileAttributes.Normal);
                    }
                    Directory.Delete(tempDir, true);
                }
                catch { }
            }
        }
    }

    [Fact]
    public void Sanitize_MasksSensitiveInformation()
    {
        var rawMessage = "Error llamando al servicio: apiKey=sk-or-v1-9876543210abcdef987654 con Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9 y password=SecretPassword123";

        var sanitized = BootLogger.Sanitize(rawMessage);

        Assert.DoesNotContain("9876543210abcdef987654", sanitized);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9", sanitized);
        Assert.DoesNotContain("SecretPassword123", sanitized);
        Assert.Contains("[REDACTED]", sanitized);
    }

    [Fact]
    public void Sanitize_PreservesNormalLogMessages()
    {
        var normalMessage = "[App] Iniciando DictaMeeting v1.5.6 (.NET 9.0.0, OS: Windows 11, 64-bit: True)";

        var sanitized = BootLogger.Sanitize(normalMessage);

        Assert.Equal(normalMessage, sanitized);
    }

    [Fact]
    public void LauncherSource_ContainsRotation_AndNoVerboseStartupNoise()
    {
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

        // Rotación a 256 KB y nombres históricos
        Assert.Contains("256 * 1024", content);
        Assert.Contains("boot.1.log", content);
        Assert.Contains("boot.2.log", content);

        // Sin mensajes ruidosos de depuración en arranques normales
        Assert.DoesNotContain("Llamando a Process.Start...", content);
        Assert.DoesNotContain("=== Arranque DictaMeeting ===", content);
        Assert.DoesNotContain("BaseDirectory: ", content);

        // Registro conciso y errores relevantes
        Assert.Contains("Proceso principal iniciado", content);
        Assert.Contains("NativeErrorCode", content);
        Assert.Contains("Win32Exception", content);
        Assert.Contains("ImmediateExitThresholdMs", content);
    }
}
