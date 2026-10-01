using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DictaMeeting.Meetings.Vocabulary;
using Xunit;

namespace DictaMeeting.Tests;

public class VocabularyServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _tempJsonFile;

    public VocabularyServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"dictameeting_vocab_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        _tempJsonFile = Path.Combine(_tempDirectory, "vocab.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task AddOrUpdateTerm_SavesAndLoadsCorrectly()
    {
        var service = new VocabularyService(_tempJsonFile);
        service.AddOrUpdateTerm("Aritz", new[] { "Arich", "Ariz" });
        await service.SaveAsync();

        var reloadedService = new VocabularyService(_tempJsonFile);
        await reloadedService.LoadAsync();

        var terms = reloadedService.GetTerms();
        Assert.Single(terms);

        var aritz = terms.FirstOrDefault(t => t.Word == "Aritz");
        Assert.NotNull(aritz);
        Assert.Contains("Arich", aritz.Aliases);
        Assert.Contains("Ariz", aritz.Aliases);
    }

    [Fact]
    public void FormatWhisperGlossaryPrompt_GeneratesCleanPrompt()
    {
        var service = new VocabularyService(_tempJsonFile);
        Assert.Equal(string.Empty, service.FormatWhisperGlossaryPrompt());

        service.AddOrUpdateTerm("Aritz");

        var prompt = service.FormatWhisperGlossaryPrompt();
        Assert.Equal("Glosario: Aritz.", prompt);
    }

    [Fact]
    public void FormatSherpaHotwords_GeneratesCommaSeparatedList()
    {
        var service = new VocabularyService(_tempJsonFile);
        service.AddOrUpdateTerm("Aritz");

        var hotwords = service.FormatSherpaHotwords();
        Assert.Equal("Aritz", hotwords);
    }

    [Fact]
    public void ReplaceAliases_ReplacesOnlyAliasesRespectingWordBoundaries()
    {
        var service = new VocabularyService(_tempJsonFile);
        service.AddOrUpdateTerm("Aritz", new[] { "arich", "ariz" });

        string input = "Hola arich y ariz, bienvenidos.";
        string output = service.ReplaceAliases(input);

        Assert.Equal("Hola Aritz y Aritz, bienvenidos.", output);
    }

    [Fact]
    public void ReplaceAliases_DoesNotCorruptSubstringsOfOtherWords()
    {
        var service = new VocabularyService(_tempJsonFile);
        // Alias que es subcadena de otra palabra legítima
        service.AddOrUpdateTerm("Aritz", new[] { "itz" });

        string input = "Hoy toca ir al blitz para ver.";
        string output = service.ReplaceAliases(input);

        // "blitz" no debe convertirse en "blAritz"
        Assert.Contains("blitz", output);
    }

    [Fact]
    public async Task ExportAndImport_PreservesAndMergesTerms()
    {
        var service1 = new VocabularyService(_tempJsonFile);
        service1.AddOrUpdateTerm("Aritz", new[] { "arich" });

        string exportFile = Path.Combine(_tempDirectory, "export.json");
        await service1.ExportToFileAsync(exportFile);

        var service2 = new VocabularyService(Path.Combine(_tempDirectory, "vocab2.json"));
        service2.AddOrUpdateTerm("Aritz", new[] { "ariz" }); // mismo término con variante adicional

        // Importar con merge = true
        await service2.ImportFromFileAsync(exportFile, merge: true);

        var terms = service2.GetTerms();
        Assert.Single(terms);

        var aritz = terms.First(t => t.Word == "Aritz");
        Assert.Contains("arich", aritz.Aliases);
        Assert.Contains("ariz", aritz.Aliases); // Se unificaron las variantes
    }
}
