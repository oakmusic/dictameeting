using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DictaMeeting.App.Common;
using DictaMeeting.App.Diagnostics;
using DictaMeeting.App.Services;
using DictaMeeting.App.ViewModels;
using DictaMeeting.Infrastructure.Persistence;
using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Exporters;
using DictaMeeting.Meetings.Interfaces;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Meetings.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DictaMeeting.Tests;

public class ApplicationStabilityAndCrashDiagnosticsTests
{
    [Fact]
    public void CrashDiagnosticTracker_OperationScope_SetsAndRestoresContext()
    {
        using (CrashDiagnosticTracker.BeginOperation("TestOperation1", "meet-123", "corr-abc"))
        {
            Assert.Equal("TestOperation1", CrashDiagnosticTracker.CurrentOperation);
            Assert.Equal("meet-123", CrashDiagnosticTracker.CurrentMeetingId);
            Assert.Equal("corr-abc", CrashDiagnosticTracker.CorrelationId);

            using (CrashDiagnosticTracker.BeginOperation("NestedOperation", "meet-999", "corr-nested"))
            {
                Assert.Equal("NestedOperation", CrashDiagnosticTracker.CurrentOperation);
                Assert.Equal("meet-999", CrashDiagnosticTracker.CurrentMeetingId);
                Assert.Equal("corr-nested", CrashDiagnosticTracker.CorrelationId);
            }

            Assert.Equal("TestOperation1", CrashDiagnosticTracker.CurrentOperation);
            Assert.Equal("meet-123", CrashDiagnosticTracker.CurrentMeetingId);
            Assert.Equal("corr-abc", CrashDiagnosticTracker.CorrelationId);
        }
    }

    [Fact]
    public void CrashDiagnosticTracker_Breadcrumbs_MaintainsCapacityAndSanitizes()
    {
        for (int i = 0; i < 50; i++)
        {
            CrashDiagnosticTracker.AddBreadcrumb($"Breadcrumb {i} with key sk-1234567890abcdef");
        }

        // Generate crash report to inspect breadcrumbs output
        var tempDir = Path.Combine(Path.GetTempPath(), "DictaMeeting_Test_Crash_" + Guid.NewGuid().ToString("N"));
        try
        {
            var reportPath = CrashDiagnosticTracker.RecordCrashReport(
                new InvalidOperationException("Test crash"),
                "UnitTest",
                isTerminating: false,
                customDirectory: tempDir);

            Assert.NotNull(reportPath);
            Assert.True(File.Exists(reportPath));

            var content = File.ReadAllText(reportPath);
            Assert.Contains("INFORME DE CIERRE INESPERADO", content);
            Assert.Contains("Test crash", content);
            Assert.Contains("sk-***[REDACTED]***", content);
            Assert.DoesNotContain("sk-1234567890abcdef", content);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public void CrashDiagnosticTracker_PrunesOldReports_WhenExceedingLimit()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DictaMeeting_Test_Prune_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            for (int i = 0; i < 15; i++)
            {
                CrashDiagnosticTracker.RecordCrashReport(
                    new Exception($"Crash {i}"),
                    "UnitTest",
                    isTerminating: false,
                    customDirectory: tempDir);

                Thread.Sleep(20); // Garantizar marcas de tiempo distintas
            }

            var files = Directory.GetFiles(tempDir, "crash_*.log");
            Assert.InRange(files.Length, 1, 10);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public void CrashDiagnosticTracker_NeverThrows_WhenDirectoryIsReadOnlyOrFails()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DictaMeeting_Test_ReadOnly_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // Crear archivo con el mismo nombre que la carpeta de reportes para forzar fallo de creación
            var dummyFile = Path.Combine(tempDir, "crash_reports");
            File.WriteAllText(dummyFile, "dummy");

            // No debe lanzar excepción
            var result = CrashDiagnosticTracker.RecordCrashReport(
                new Exception("Test error"),
                "UnitTest",
                isTerminating: false,
                customDirectory: dummyFile);

            Assert.Null(result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public void FileLoggerProvider_WritesSanitizedLogs_AndRotates()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DictaMeeting_Test_Logger_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var provider = new DictaMeetingFileLoggerProvider(tempDir);
            var logger = provider.CreateLogger("TestCategory");

            logger.LogInformation("Mensaje informativo con sk-proj-1234567890abcdef");
            logger.LogError(new InvalidOperationException("Fallo en servicio"), "Error crítico");

            var logFile = Path.Combine(tempDir, "app.log");
            Assert.True(File.Exists(logFile));

            var content = File.ReadAllText(logFile);
            Assert.Contains("[INF] [TestCategory]", content);
            Assert.Contains("[ERR] [TestCategory]", content);
            Assert.Contains("sk-***[REDACTED]***", content);
            Assert.DoesNotContain("sk-proj-1234567890abcdef", content);
            Assert.Contains("InvalidOperationException", content);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task SafeFireAndForget_ObservesAndLogsTaskException()
    {
        bool loggedError = false;
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new TestCallbackLoggerProvider((_, level, _) =>
            {
                if (level == LogLevel.Error) loggedError = true;
            }));
        });

        var logger = loggerFactory.CreateLogger("Test");

        var tcs = new TaskCompletionSource();
        tcs.SetException(new ApplicationException("Error simulado en tarea de fondo"));

        tcs.Task.SafeFireAndForget(logger, "TareaDePrueba");

        await Task.Delay(150); // Dar margen al hilo de continuación
        Assert.True(loggedError);
    }

    [Fact]
    public async Task LocalFileMeetingRepository_ConcurrentSavesAndReads_DoNotCollide()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DictaMeeting_Test_RepoConcurrency_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var exporter = new DocumentExporter();
            var repo = new LocalFileMeetingRepository(exporter, tempDir);

            var meeting = new Meeting
            {
                Id = "meet-concurrent",
                Title = "Reunión Concurrente",
                State = MeetingState.Completed,
                Date = DateTime.Now,
                Transcript = new() { new() { Text = "Texto inicial" } }
            };

            await repo.SaveMeetingAsync(meeting);

            // Ejecutar múltiples escrituras y lecturas concurrentes simultáneas
            var tasks = new Task[10];
            for (int i = 0; i < 5; i++)
            {
                int index = i;
                tasks[i] = Task.Run(async () =>
                {
                    meeting.Transcript.Add(new() { Text = $"Segmento {index}" });
                    await repo.SaveMeetingAsync(meeting);
                });
            }

            for (int i = 5; i < 10; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    var all = await repo.GetAllMeetingsAsync();
                    Assert.NotEmpty(all);
                });
            }

            await Task.WhenAll(tasks);

            var loaded = await repo.GetMeetingAsync("meet-concurrent");
            Assert.NotNull(loaded);
            Assert.Equal("Reunión Concurrente", loaded.Title);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task MainViewModel_RapidHistoryAndWorkspaceNavigation_DoesNotThrow()
    {
        var (vm, repo, _) = MeetingWorkspaceNavigationTests.CreateTestFixture();

        var m1 = new Meeting { Id = "m1", Title = "Reunión 1", State = MeetingState.Completed, Date = DateTime.Now };
        var m2 = new Meeting { Id = "m2", Title = "Reunión 2", State = MeetingState.Completed, Date = DateTime.Now.AddHours(-1) };

        await repo.SaveMeetingAsync(m1);
        await repo.SaveMeetingAsync(m2);

        // Simular clic rápido repetido en pestaña de historial
        vm.SelectHistoryTabCommand.Execute(null);
        vm.SelectHistoryTabCommand.Execute(null);

        await vm.LoadSavedMeetingsAsync();

        Assert.Equal(2, vm.SavedMeetings.Count);

        // Simular apertura rápida de la reunión más reciente
        var firstItem = vm.SavedMeetings[0];
        await vm.OpenMeetingCommand.ExecuteAsync(firstItem);

        Assert.Equal(0, vm.SelectedNavigationTab);
        Assert.Equal(firstItem.Title, vm.MeetingTitle);
    }

    private sealed class TestCallbackLoggerProvider : ILoggerProvider
    {
        private readonly Action<string, LogLevel, string> _callback;

        public TestCallbackLoggerProvider(Action<string, LogLevel, string> callback)
        {
            _callback = callback;
        }

        public ILogger CreateLogger(string categoryName) => new TestCallbackLogger(categoryName, _callback);

        public void Dispose() { }

        private sealed class TestCallbackLogger : ILogger
        {
            private readonly string _category;
            private readonly Action<string, LogLevel, string> _callback;

            public TestCallbackLogger(string category, Action<string, LogLevel, string> callback)
            {
                _category = category;
                _callback = callback;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                _callback(_category, logLevel, formatter(state, exception));
            }
        }
    }
}
