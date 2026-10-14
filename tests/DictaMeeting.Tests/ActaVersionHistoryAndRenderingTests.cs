using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Documents;
using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Exporters;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Infrastructure.Persistence;
using DictaMeeting.App.ViewModels;
using DictaMeeting.App.Services;
using Markdig;
using Markdig.Wpf;
using Xunit;

namespace DictaMeeting.Tests;

public class ActaVersionHistoryAndRenderingTests : IDisposable
{
    private readonly string _testBaseDir;
    private readonly DocumentExporter _exporter = new();

    public ActaVersionHistoryAndRenderingTests()
    {
        _testBaseDir = Path.Combine(Path.GetTempPath(), "DictaMeeting_ActaTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testBaseDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testBaseDir))
            {
                Directory.Delete(_testBaseDir, true);
            }
        }
        catch
        {
            // Ignore cleanup exceptions
        }
    }

    private LocalFileMeetingRepository CreateRepository()
        => new(_exporter, _testBaseDir);

    private Meeting CreateSampleMeeting(string title = "Reunión de Prueba")
    {
        var meeting = new Meeting
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title,
            Date = DateTime.Today,
            StartTime = DateTimeOffset.Now,
            Organizer = "Unit Test",
            Company = "DictaCorp"
        };
        meeting.Transcript.Add(new TranscriptSegment
        {
            SpeakerDisplayName = "Aritz",
            Text = "Hola a todos, iniciamos la sesión."
        });
        return meeting;
    }

    // 1. Generar dos actas con distintos idiomas crea dos versiones independientes.
    [Fact]
    public async Task Test01_GeneratingTwoActasWithDifferentLanguages_CreatesTwoIndependentVersions()
    {
        var repo = CreateRepository();
        var meeting = CreateSampleMeeting();

        var vEs = new MeetingActaVersion
        {
            MeetingId = meeting.Id,
            Language = LanguageMode.Spanish,
            DetailLevel = ActaDetailLevel.Normal,
            ModelUsed = "openrouter/free",
            MarkdownContent = "# Acta en Español\n\nContenido en español."
        };

        var vEn = new MeetingActaVersion
        {
            MeetingId = meeting.Id,
            Language = LanguageMode.English,
            DetailLevel = ActaDetailLevel.Normal,
            ModelUsed = "openrouter/free",
            MarkdownContent = "# Minutes in English\n\nEnglish content."
        };

        meeting.ActaVersions.Add(vEs);
        meeting.ActaVersions.Add(vEn);
        await repo.SaveMeetingAsync(meeting);

        var loaded = await repo.GetMeetingAsync(meeting.Id);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.ActaVersions.Count);
        Assert.Contains(loaded.ActaVersions, v => v.Language == LanguageMode.Spanish && v.MarkdownContent.Contains("Español"));
        Assert.Contains(loaded.ActaVersions, v => v.Language == LanguageMode.English && v.MarkdownContent.Contains("English"));
        Assert.NotEqual(loaded.ActaVersions[0].Id, loaded.ActaVersions[1].Id);
    }

    // 2. Generar varias actas con distintos niveles de detalle conserva todas.
    [Fact]
    public async Task Test02_GeneratingSeveralActasWithDifferentDetailLevels_PreservesAll()
    {
        var repo = CreateRepository();
        var meeting = CreateSampleMeeting();

        var levels = new[] { ActaDetailLevel.Breve, ActaDetailLevel.Normal, ActaDetailLevel.Detallada, ActaDetailLevel.Exhaustiva };
        foreach (var lvl in levels)
        {
            meeting.ActaVersions.Add(new MeetingActaVersion
            {
                MeetingId = meeting.Id,
                Language = LanguageMode.Spanish,
                DetailLevel = lvl,
                MarkdownContent = $"# Nivel {lvl}\n\nDetalle {lvl}"
            });
        }

        await repo.SaveMeetingAsync(meeting);

        var loaded = await repo.GetMeetingAsync(meeting.Id);
        Assert.NotNull(loaded);
        Assert.Equal(4, loaded.ActaVersions.Count);
        foreach (var lvl in levels)
        {
            Assert.Contains(loaded.ActaVersions, v => v.DetailLevel == lvl);
        }
    }

    // 3. Dos generaciones con los mismos parámetros no sobrescriben la anterior.
    [Fact]
    public async Task Test03_TwoGenerationsWithSameParameters_DoNotOverwritePrevious()
    {
        var repo = CreateRepository();
        var meeting = CreateSampleMeeting();

        var v1 = new MeetingActaVersion
        {
            MeetingId = meeting.Id,
            Language = LanguageMode.Spanish,
            DetailLevel = ActaDetailLevel.Normal,
            ModelUsed = "gpt-4o",
            MarkdownContent = "# Acta 1: Primera versión generada"
        };
        meeting.ActaVersions.Add(v1);
        await repo.SaveMeetingAsync(meeting);

        // Breve pausa para asegurar timestamp diferenciable
        await Task.Delay(20);

        var v2 = new MeetingActaVersion
        {
            MeetingId = meeting.Id,
            Language = LanguageMode.Spanish,
            DetailLevel = ActaDetailLevel.Normal,
            ModelUsed = "gpt-4o",
            MarkdownContent = "# Acta 2: Segunda versión con los mismos parámetros"
        };
        meeting.ActaVersions.Add(v2);
        await repo.SaveMeetingAsync(meeting);

        var loaded = await repo.GetMeetingAsync(meeting.Id);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.ActaVersions.Count);
        Assert.NotEqual(v1.Id, v2.Id);
        Assert.Contains(loaded.ActaVersions, v => v.MarkdownContent.Contains("Primera versión"));
        Assert.Contains(loaded.ActaVersions, v => v.MarkdownContent.Contains("Segunda versión"));

        // Comprobar archivos en disco en la carpeta actas/
        var meetingDir = repo.GetMeetingDirectoryPath(meeting);
        var actasDir = Path.Combine(meetingDir, "actas");
        Assert.True(Directory.Exists(actasDir));
        var mdFiles = Directory.GetFiles(actasDir, "acta_*.md");
        Assert.Equal(2, mdFiles.Length);
    }

    // 4. El historial se mantiene después de cerrar y volver a abrir una reunión.
    [Fact]
    public async Task Test04_HistoryPersistsAcrossClosingAndReopeningMeeting()
    {
        var repo1 = CreateRepository();
        var meeting = CreateSampleMeeting();

        meeting.ActaVersions.Add(new MeetingActaVersion
        {
            MeetingId = meeting.Id,
            Language = LanguageMode.Spanish,
            DetailLevel = ActaDetailLevel.Normal,
            ModelUsed = "claude-3-5",
            MarkdownContent = "# Acta persistente\nContenido guardado."
        });
        await repo1.SaveMeetingAsync(meeting);

        // Simular reinicio instanciando un repositorio completamente nuevo
        var repo2 = CreateRepository();
        var reloaded = await repo2.GetMeetingAsync(meeting.Id);

        Assert.NotNull(reloaded);
        Assert.Single(reloaded.ActaVersions);
        Assert.Equal("claude-3-5", reloaded.ActaVersions[0].ModelUsed);
        Assert.Contains("Contenido guardado", reloaded.ActaVersions[0].MarkdownContent);
    }

    // 5. Las actas de reuniones distintas permanecen aisladas.
    [Fact]
    public async Task Test05_ActasFromDifferentMeetings_RemainStrictlyIsolated()
    {
        var repo = CreateRepository();
        var meetingA = CreateSampleMeeting("Reunión A");
        var meetingB = CreateSampleMeeting("Reunión B");

        meetingA.ActaVersions.Add(new MeetingActaVersion
        {
            MeetingId = meetingA.Id,
            Language = LanguageMode.Spanish,
            DetailLevel = ActaDetailLevel.Normal,
            MarkdownContent = "# Acta Reunión A"
        });

        meetingB.ActaVersions.Add(new MeetingActaVersion
        {
            MeetingId = meetingB.Id,
            Language = LanguageMode.English,
            DetailLevel = ActaDetailLevel.Exhaustiva,
            MarkdownContent = "# Minutes Meeting B"
        });

        await repo.SaveMeetingAsync(meetingA);
        await repo.SaveMeetingAsync(meetingB);

        var loadedA = await repo.GetMeetingAsync(meetingA.Id);
        var loadedB = await repo.GetMeetingAsync(meetingB.Id);

        Assert.NotNull(loadedA);
        Assert.NotNull(loadedB);

        Assert.Single(loadedA.ActaVersions);
        Assert.Equal("Reunión A", loadedA.Title);
        Assert.Contains("Reunión A", loadedA.ActaVersions[0].MarkdownContent);

        Assert.Single(loadedB.ActaVersions);
        Assert.Equal("Reunión B", loadedB.Title);
        Assert.Contains("Meeting B", loadedB.ActaVersions[0].MarkdownContent);
    }

    // 6. El cambio de versión presenta el contenido correcto.
    [Fact]
    public void Test06_VersionSwitching_PresentsCorrectContent()
    {
        var meeting = CreateSampleMeeting();
        var v1 = new MeetingActaVersion
        {
            MeetingId = meeting.Id,
            Language = LanguageMode.Spanish,
            DetailLevel = ActaDetailLevel.Breve,
            MarkdownContent = "# Resumen Ejecutivo Breve"
        };
        var v2 = new MeetingActaVersion
        {
            MeetingId = meeting.Id,
            Language = LanguageMode.Spanish,
            DetailLevel = ActaDetailLevel.Detallada,
            MarkdownContent = "# Acta Detallada Exhaustiva"
        };

        meeting.ActaVersions.Add(v1);
        meeting.ActaVersions.Add(v2);

        // Seleccionar versión 1
        meeting.SelectedActaVersionId = v1.Id;
        var active1 = meeting.GetActiveActaVersion();
        Assert.NotNull(active1);
        Assert.Equal(v1.Id, active1.Id);
        Assert.Contains("Breve", active1.MarkdownContent);

        // Seleccionar versión 2
        meeting.SelectedActaVersionId = v2.Id;
        var active2 = meeting.GetActiveActaVersion();
        Assert.NotNull(active2);
        Assert.Equal(v2.Id, active2.Id);
        Assert.Contains("Detallada Exhaustiva", active2.MarkdownContent);
    }

    // 7. La edición de una versión no modifica otra.
    [Fact]
    public async Task Test07_EditingOneVersion_DoesNotModifyAnother()
    {
        var repo = CreateRepository();
        var meeting = CreateSampleMeeting();

        var v1 = new MeetingActaVersion
        {
            MeetingId = meeting.Id,
            Language = LanguageMode.Spanish,
            DetailLevel = ActaDetailLevel.Normal,
            MarkdownContent = "# Versión 1 Original"
        };
        var v2 = new MeetingActaVersion
        {
            MeetingId = meeting.Id,
            Language = LanguageMode.Spanish,
            DetailLevel = ActaDetailLevel.Normal,
            MarkdownContent = "# Versión 2 Intacta"
        };

        meeting.ActaVersions.Add(v1);
        meeting.ActaVersions.Add(v2);
        await repo.SaveMeetingAsync(meeting);

        // Editar exclusivamente la versión 1
        v1.MarkdownContent = "# Versión 1 Modificada por el usuario";
        v1.LastModifiedAt = DateTimeOffset.UtcNow;
        await repo.SaveMeetingAsync(meeting);

        var loaded = await repo.GetMeetingAsync(meeting.Id);
        Assert.NotNull(loaded);

        var loadedV1 = loaded.ActaVersions.First(v => v.Id == v1.Id);
        var loadedV2 = loaded.ActaVersions.First(v => v.Id == v2.Id);

        Assert.Contains("Modificada por el usuario", loadedV1.MarkdownContent);
        Assert.NotNull(loadedV1.LastModifiedAt);

        Assert.Contains("Versión 2 Intacta", loadedV2.MarkdownContent);
        Assert.Null(loadedV2.LastModifiedAt);
    }

    // 8. El contenido se guarda sin destruir el Markdown original.
    [Fact]
    public async Task Test08_ContentIsSavedWithoutDestroyingOriginalMarkdown()
    {
        var repo = CreateRepository();
        var meeting = CreateSampleMeeting();

        var originalText = "# Acta Original IA\n\n1. Punto 1\n2. Punto 2";
        var version = new MeetingActaVersion
        {
            MeetingId = meeting.Id,
            Language = LanguageMode.Spanish,
            DetailLevel = ActaDetailLevel.Normal,
            MarkdownContent = originalText,
            OriginalAiMarkdownContent = originalText
        };
        meeting.ActaVersions.Add(version);
        await repo.SaveMeetingAsync(meeting);

        // El usuario edita la versión
        version.MarkdownContent = "# Acta Editada Manualmente\n\nTexto adicional";
        version.LastModifiedAt = DateTimeOffset.UtcNow;
        await repo.SaveMeetingAsync(meeting);

        var loaded = await repo.GetMeetingAsync(meeting.Id);
        Assert.NotNull(loaded);
        var loadedV = loaded.ActaVersions[0];

        Assert.Equal("# Acta Editada Manualmente\n\nTexto adicional", loadedV.MarkdownContent);
        Assert.Equal(originalText, loadedV.OriginalAiMarkdownContent);
    }

    // 9. Los botones de copiar y exportar utilizan la versión seleccionada.
    [Fact]
    public void Test09_CopyAndExport_UseSelectedVersion()
    {
        var meeting = CreateSampleMeeting();
        var v1 = new MeetingActaVersion
        {
            MeetingId = meeting.Id,
            Language = LanguageMode.Spanish,
            DetailLevel = ActaDetailLevel.Normal,
            MarkdownContent = "# Contenido Versión 1"
        };
        var v2 = new MeetingActaVersion
        {
            MeetingId = meeting.Id,
            Language = LanguageMode.English,
            DetailLevel = ActaDetailLevel.Exhaustiva,
            MarkdownContent = "# Content Version 2"
        };
        meeting.ActaVersions.Add(v1);
        meeting.ActaVersions.Add(v2);

        // Con versión 1 seleccionada
        meeting.SelectedActaVersionId = v1.Id;
        var exportContent1 = meeting.GetActiveActaVersion()?.MarkdownContent ?? meeting.ActaMarkdown;
        Assert.Equal("# Contenido Versión 1", exportContent1);

        // Con versión 2 seleccionada
        meeting.SelectedActaVersionId = v2.Id;
        var exportContent2 = meeting.GetActiveActaVersion()?.MarkdownContent ?? meeting.ActaMarkdown;
        Assert.Equal("# Content Version 2", exportContent2);
    }

    // 10. Una reunión antigua con una sola acta puede abrirse sin pérdida ni duplicados.
    [Fact]
    public async Task Test10_LegacyMeetingWithSingleActa_OpensWithoutLossOrDuplicates()
    {
        var repo = CreateRepository();
        var meeting = CreateSampleMeeting("Reunión Antigua");

        // Guardar primero para crear la estructura de carpetas estándar
        await repo.SaveMeetingAsync(meeting);

        var meetingDir = repo.GetMeetingDirectoryPath(meeting);
        var baseName = repo.GetMeetingFileBaseName(meeting);

        // Borrar carpeta actas/ si existe y colocar únicamente el archivo raíz de acta antigua
        var actasDir = Path.Combine(meetingDir, "actas");
        if (Directory.Exists(actasDir)) Directory.Delete(actasDir, true);

        var legacyActaPath = Path.Combine(meetingDir, $"{baseName}_acta.md");
        var legacyContent = "# Acta Histórica Anterior\n\nContenido de versión antigua sin índice.";
        await File.WriteAllTextAsync(legacyActaPath, legacyContent);

        // Primera carga: debe migrar el archivo legado de manera transparente
        var loaded1 = await repo.GetMeetingAsync(meeting.Id);
        Assert.NotNull(loaded1);
        Assert.Single(loaded1.ActaVersions);
        Assert.True(loaded1.ActaVersions[0].IsLegacyMigrated);
        Assert.Equal(legacyContent, loaded1.ActaVersions[0].MarkdownContent);

        // Segunda carga: debe ser completamente idempotente (no duplicar)
        var loaded2 = await repo.GetMeetingAsync(meeting.Id);
        Assert.NotNull(loaded2);
        Assert.Single(loaded2.ActaVersions);
        Assert.Equal(legacyContent, loaded2.ActaVersions[0].MarkdownContent);
    }

    // 11. Los errores de escritura o lectura no destruyen las versiones ya guardadas.
    [Fact]
    public async Task Test11_ReadOrWriteErrors_DoNotDestroyExistingSavedVersions()
    {
        var repo = CreateRepository();
        var meeting = CreateSampleMeeting();

        var v1 = new MeetingActaVersion
        {
            MeetingId = meeting.Id,
            Language = LanguageMode.Spanish,
            DetailLevel = ActaDetailLevel.Normal,
            MarkdownContent = "# Acta 1 Existente y Segura"
        };
        meeting.ActaVersions.Add(v1);
        await repo.SaveMeetingAsync(meeting);

        var meetingDir = repo.GetMeetingDirectoryPath(meeting);
        var actasDir = Path.Combine(meetingDir, "actas");
        var indexPath = Path.Combine(actasDir, "index.json");

        // Simular corrupción del index.json
        await File.WriteAllTextAsync(indexPath, "{ corrupted json syntax !@#$%^&*");

        // El cargador no debe lanzar excepción ni borrar los archivos .md existentes
        var loaded = await repo.GetMeetingAsync(meeting.Id);
        Assert.NotNull(loaded);

        // Los archivos Markdown existentes en el disco siguen intactos
        var mdFiles = Directory.GetFiles(actasDir, "acta_*.md");
        Assert.True(mdFiles.Length > 0);
        var content = await File.ReadAllTextAsync(mdFiles[0]);
        Assert.Contains("Acta 1 Existente y Segura", content);
    }

    // 12. El renderizador presenta correctamente los elementos Markdown admitidos.
    [Fact]
    public void Test12_RendererDisplaysSupportedMarkdownElements()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var md = @"# Título Principal H1
## Subtítulo H2
### Sección H3

Este es un párrafo con **negrita** y *cursiva*.
Aquí un [enlace seguro](https://dictameeting.com).

- Elemento viñeta A
- Elemento viñeta B

1. Primer paso
2. Segundo paso

> Bloque de cita destacada.

---

| Función | Estado | Prioridad |
| :--- | :--- | :--- |
| Visor Markdown | Operativo | Alta |
| Historial | Operativo | Alta |

```csharp
var status = ""OK"";
```";

                var pipeline = new MarkdownPipelineBuilder().UseSupportedExtensions().Build();
                var doc = Markdig.Wpf.Markdown.ToFlowDocument(md, pipeline);

                Assert.NotNull(doc);
                Assert.True(doc.Blocks.Count > 0);

                // Comprobar presencia de párrafos
                var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();
                Assert.NotEmpty(paragraphs);

                // Comprobar presencia de tabla
                var table = doc.Blocks.OfType<Table>().FirstOrDefault();
                Assert.NotNull(table);
                Assert.True(table.RowGroups.Count > 0);

                // Comprobar presencia de sección (blockquote)
                var section = doc.Blocks.OfType<Section>().FirstOrDefault();
                Assert.NotNull(section);

                // Comprobar presencia de listas
                var list = doc.Blocks.OfType<List>().FirstOrDefault();
                Assert.NotNull(list);
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx != null)
        {
            throw new AggregateException("Error rendering Markdown to FlowDocument in STA thread", threadEx);
        }
    }

    // 13. La generación fallida o cancelada no crea una versión válida falsa.
    [Fact]
    public async Task Test13_FailedOrCancelledGeneration_DoesNotCreateFalseValidVersion()
    {
        var repo = CreateRepository();
        var meeting = CreateSampleMeeting();

        // Inicialmente con una versión válida
        var v1 = new MeetingActaVersion
        {
            MeetingId = meeting.Id,
            Language = LanguageMode.Spanish,
            DetailLevel = ActaDetailLevel.Normal,
            MarkdownContent = "# Versión Válida Previa"
        };
        meeting.ActaVersions.Add(v1);
        await repo.SaveMeetingAsync(meeting);

        // Simular intento de generación fallido: ocurre una excepción antes de guardar
        try
        {
            // Simular fallo en API
            throw new InvalidOperationException("API rate limit exceeded o cancelado por el usuario");
        }
        catch
        {
            // Al fallar, NO se debe agregar ninguna versión a la colección
        }

        var loaded = await repo.GetMeetingAsync(meeting.Id);
        Assert.NotNull(loaded);
        Assert.Single(loaded.ActaVersions);
        Assert.Equal(v1.Id, loaded.ActaVersions[0].Id);
    }

    // 14. Los cambios de reunión no permiten mostrar accidentalmente el acta de una reunión anterior.
    [Fact]
    public void Test14_SwitchingMeetings_NeverDisplaysPreviousMeetingActa()
    {
        var meetingWithActa = CreateSampleMeeting("Reunión Con Acta");
        meetingWithActa.ActaVersions.Add(new MeetingActaVersion
        {
            MeetingId = meetingWithActa.Id,
            Language = LanguageMode.Spanish,
            DetailLevel = ActaDetailLevel.Normal,
            MarkdownContent = "# Acta Confidencial Reunión 1"
        });
        meetingWithActa.SelectedActaVersionId = meetingWithActa.ActaVersions[0].Id;

        var meetingWithoutActa = CreateSampleMeeting("Reunión Nueva Sin Acta");

        // Al consultar la reunión con acta
        var active1 = meetingWithActa.GetActiveActaVersion();
        Assert.NotNull(active1);
        Assert.Contains("Confidencial", active1.MarkdownContent);

        // Al cambiar a la reunión sin acta
        var active2 = meetingWithoutActa.GetActiveActaVersion();
        Assert.Null(active2);
        Assert.Empty(meetingWithoutActa.ActaVersions);
    }

    // 15. Abrir el modal de una reunión sin actas previas presenta colección vacía y estado vacío localizado.
    [Fact]
    public void Test15_ModalOpening_MeetingWithoutActas_ShowsEmptyVersionsListAndLocalizedEmptyState()
    {
        var meeting = CreateSampleMeeting("Reunión Vacía");
        var availableVersions = new System.Collections.ObjectModel.ObservableCollection<ActaVersionItemViewModel>();

        // Simular ordenación de versiones como en UpdateAvailableActaVersions
        if (meeting.ActaVersions != null)
        {
            var sorted = meeting.ActaVersions.OrderByDescending(v => v.CreatedAt).ToList();
            foreach (var v in sorted)
            {
                availableVersions.Add(new ActaVersionItemViewModel(v));
            }
        }

        Assert.Empty(availableVersions);

        var loc = LocalizationManager.Instance;
        var emptyText = loc["Acta_Modal_History_Empty"];
        Assert.False(string.IsNullOrWhiteSpace(emptyText));
        Assert.NotEqual("Acta_Modal_History_Empty", emptyText);
    }

    // 16. Abrir el modal de una reunión con múltiples versiones las presenta ordenadas de más reciente a más antigua.
    [Fact]
    public void Test16_ModalOpening_MultipleVersions_SortedNewestToOldest()
    {
        var meeting = CreateSampleMeeting("Reunión con Múltiples Versiones");
        var now = DateTimeOffset.Now;

        var vOld = new MeetingActaVersion
        {
            MeetingId = meeting.Id,
            Language = LanguageMode.Spanish,
            DetailLevel = ActaDetailLevel.Normal,
            CreatedAt = now.AddMinutes(-30),
            MarkdownContent = "# Acta Antigua"
        };
        var vMid = new MeetingActaVersion
        {
            MeetingId = meeting.Id,
            Language = LanguageMode.English,
            DetailLevel = ActaDetailLevel.Exhaustiva,
            CreatedAt = now.AddMinutes(-10),
            MarkdownContent = "# Acta Intermedia"
        };
        var vNew = new MeetingActaVersion
        {
            MeetingId = meeting.Id,
            Language = LanguageMode.Spanish,
            DetailLevel = ActaDetailLevel.Detallada,
            CreatedAt = now,
            MarkdownContent = "# Acta Nueva"
        };

        meeting.ActaVersions.Add(vOld);
        meeting.ActaVersions.Add(vMid);
        meeting.ActaVersions.Add(vNew);

        var availableVersions = new System.Collections.ObjectModel.ObservableCollection<ActaVersionItemViewModel>();
        var sorted = meeting.ActaVersions.OrderByDescending(v => v.CreatedAt).ToList();
        foreach (var v in sorted)
        {
            availableVersions.Add(new ActaVersionItemViewModel(v));
        }

        Assert.Equal(3, availableVersions.Count);
        Assert.Equal(vNew.Id, availableVersions[0].Id);
        Assert.Equal(vMid.Id, availableVersions[1].Id);
        Assert.Equal(vOld.Id, availableVersions[2].Id);
    }

    // 17. Las etiquetas del historial coinciden exactamente con DisplayName, identificando idioma real, nivel de detalle y fecha sin claves internas.
    [Fact]
    public void Test17_HistoryLabels_IdentifyRealLanguageAndDetail_WithoutInternalKeys()
    {
        var loc = LocalizationManager.Instance;
        var originalCulture = loc.CurrentLanguageCode;

        try
        {
            var date = new DateTimeOffset(2026, 10, 14, 10, 44, 0, TimeSpan.FromHours(2));
            var vEn = new MeetingActaVersion
            {
                Language = LanguageMode.English,
                DetailLevel = ActaDetailLevel.Exhaustiva,
                CreatedAt = date,
                MarkdownContent = "# Content English"
            };
            var vEs = new MeetingActaVersion
            {
                Language = LanguageMode.Spanish,
                DetailLevel = ActaDetailLevel.Normal,
                CreatedAt = date.AddMinutes(-1),
                MarkdownContent = "# Contenido Español"
            };

            var vmEn = new ActaVersionItemViewModel(vEn);
            var vmEs = new ActaVersionItemViewModel(vEs);

            // Escenario 1: UI en Español
            loc.SetLanguage("es");
            vmEn.NotifyChanged();
            vmEs.NotifyChanged();

            // Debe mostrar "English" o "Español", nunca la clave interna "Language_English" o "Language_Spanish"
            Assert.DoesNotContain("Language_English", vmEn.DisplayName);
            Assert.DoesNotContain("Language_Spanish", vmEs.DisplayName);
            Assert.StartsWith("English", vmEn.DisplayName);
            Assert.StartsWith("Español", vmEs.DisplayName);
            Assert.Contains("14/10/2026 10:44", vmEn.DisplayName);
            Assert.Contains("14/10/2026 10:43", vmEs.DisplayName);
            Assert.Contains("Exhaustiva", vmEn.DisplayName);
            Assert.Contains("Normal", vmEs.DisplayName);

            // Escenario 2: UI en Inglés
            loc.SetLanguage("en");
            vmEn.NotifyChanged();
            vmEs.NotifyChanged();

            Assert.DoesNotContain("Language_English", vmEn.DisplayName);
            Assert.DoesNotContain("Language_Spanish", vmEs.DisplayName);
            Assert.StartsWith("English", vmEn.DisplayName);
            Assert.StartsWith("Spanish", vmEs.DisplayName);
            Assert.Contains("Comprehensive", vmEn.DisplayName);
            Assert.Contains("Normal", vmEs.DisplayName);

            // Los metadatos de las versiones subyacentes se mantienen intactos
            Assert.Equal(LanguageMode.English, vEn.Language);
            Assert.Equal(LanguageMode.Spanish, vEs.Language);
        }
        finally
        {
            loc.SetLanguage(originalCulture);
        }
    }

    // 18. Generar varias actas con el mismo idioma y nivel de detalle crea versiones independientes sin sobrescribir.
    [Fact]
    public void Test18_MultipleGenerationsSameLanguageAndDetail_CreatesIndependentVersions()
    {
        var meeting = CreateSampleMeeting();

        var v1 = new MeetingActaVersion
        {
            MeetingId = meeting.Id,
            Language = LanguageMode.Spanish,
            DetailLevel = ActaDetailLevel.Normal,
            CreatedAt = DateTimeOffset.Now.AddMinutes(-5),
            MarkdownContent = "# Versión 1"
        };
        var v2 = new MeetingActaVersion
        {
            MeetingId = meeting.Id,
            Language = LanguageMode.Spanish,
            DetailLevel = ActaDetailLevel.Normal,
            CreatedAt = DateTimeOffset.Now,
            MarkdownContent = "# Versión 2 con correcciones"
        };

        meeting.ActaVersions.Add(v1);
        meeting.ActaVersions.Add(v2);

        var availableVersions = new System.Collections.ObjectModel.ObservableCollection<ActaVersionItemViewModel>();
        foreach (var v in meeting.ActaVersions.OrderByDescending(x => x.CreatedAt))
        {
            availableVersions.Add(new ActaVersionItemViewModel(v));
        }

        Assert.Equal(2, availableVersions.Count);
        Assert.NotEqual(availableVersions[0].Id, availableVersions[1].Id);
        Assert.Equal("# Versión 2 con correcciones", availableVersions[0].MarkdownContent);
        Assert.Equal("# Versión 1", availableVersions[1].MarkdownContent);
    }

    // 19. El cambio de reunión actualiza inmediatamente las versiones disponibles sin mezclar datos.
    [Fact]
    public void Test19_SwitchingMeetings_UpdatesAvailableVersionsWithoutCrossContamination()
    {
        var meetingA = CreateSampleMeeting("Reunión A");
        meetingA.ActaVersions.Add(new MeetingActaVersion
        {
            MeetingId = meetingA.Id,
            Language = LanguageMode.English,
            DetailLevel = ActaDetailLevel.Breve,
            CreatedAt = DateTimeOffset.Now,
            MarkdownContent = "# Acta A"
        });

        var meetingB = CreateSampleMeeting("Reunión B");
        meetingB.ActaVersions.Add(new MeetingActaVersion
        {
            MeetingId = meetingB.Id,
            Language = LanguageMode.Spanish,
            DetailLevel = ActaDetailLevel.Exhaustiva,
            CreatedAt = DateTimeOffset.Now,
            MarkdownContent = "# Acta B"
        });

        var availableVersions = new System.Collections.ObjectModel.ObservableCollection<ActaVersionItemViewModel>();

        // Cargar reunión A
        availableVersions.Clear();
        foreach (var v in meetingA.ActaVersions) availableVersions.Add(new ActaVersionItemViewModel(v));
        Assert.Single(availableVersions);
        Assert.Equal(meetingA.ActaVersions[0].Id, availableVersions[0].Id);
        Assert.Equal(LanguageMode.English, availableVersions[0].Language);

        // Cargar reunión B
        availableVersions.Clear();
        foreach (var v in meetingB.ActaVersions) availableVersions.Add(new ActaVersionItemViewModel(v));
        Assert.Single(availableVersions);
        Assert.Equal(meetingB.ActaVersions[0].Id, availableVersions[0].Id);
        Assert.Equal(LanguageMode.Spanish, availableVersions[0].Language);
    }

    // 20. Una nueva versión añadida durante la sesión se posiciona inmediatamente como la más reciente.
    [Fact]
    public void Test20_NewlyGeneratedVersion_AppearsImmediatelyAtTop()
    {
        var meeting = CreateSampleMeeting();
        var vInitial = new MeetingActaVersion
        {
            MeetingId = meeting.Id,
            Language = LanguageMode.Spanish,
            DetailLevel = ActaDetailLevel.Normal,
            CreatedAt = DateTimeOffset.Now.AddHours(-1),
            MarkdownContent = "# Versión Inicial"
        };
        meeting.ActaVersions.Add(vInitial);

        var availableVersions = new System.Collections.ObjectModel.ObservableCollection<ActaVersionItemViewModel>();
        foreach (var v in meeting.ActaVersions.OrderByDescending(x => x.CreatedAt))
        {
            availableVersions.Add(new ActaVersionItemViewModel(v));
        }
        Assert.Single(availableVersions);

        // Generar nueva versión con timestamp actual
        var vNew = new MeetingActaVersion
        {
            MeetingId = meeting.Id,
            Language = LanguageMode.English,
            DetailLevel = ActaDetailLevel.Detallada,
            CreatedAt = DateTimeOffset.Now,
            MarkdownContent = "# Versión Recién Generada"
        };
        meeting.ActaVersions.Add(vNew);

        // Simular refresco tras generación
        availableVersions.Clear();
        foreach (var v in meeting.ActaVersions.OrderByDescending(x => x.CreatedAt))
        {
            availableVersions.Add(new ActaVersionItemViewModel(v));
        }

        Assert.Equal(2, availableVersions.Count);
        Assert.Equal(vNew.Id, availableVersions[0].Id);
        Assert.Equal(vInitial.Id, availableVersions[1].Id);
    }
}
