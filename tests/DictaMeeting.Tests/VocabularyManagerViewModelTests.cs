using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DictaMeeting.App.ViewModels;
using DictaMeeting.Meetings.Vocabulary;
using Xunit;

namespace DictaMeeting.Tests;

public class VocabularyManagerViewModelTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _tempJsonFile;

    public VocabularyManagerViewModelTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"dictameeting_vm_tests_{Guid.NewGuid():N}");
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
    public void OnNewWordChanged_AutoGeneratesAliases_WithoutClickingButtons()
    {
        var service = new VocabularyService(_tempJsonFile);
        var vm = new VocabularyManagerViewModel(service);

        // Al escribir "Aritz", automáticamente debe proponer variantes como "Arich"
        vm.NewWord = "Aritz";
        Assert.Contains("Arich", vm.NewWordAliasesText, StringComparison.OrdinalIgnoreCase);

        // Al vaciar, se limpia
        vm.NewWord = string.Empty;
        Assert.Equal(string.Empty, vm.NewWordAliasesText);
    }

    [Fact]
    public async Task AddOrUpdateTermAsync_AddsTermAndResetsInputFields()
    {
        var service = new VocabularyService(_tempJsonFile);
        var vm = new VocabularyManagerViewModel(service);

        vm.NewWord = "Aritz";
        // NewWordAliasesText ya tiene sugerencias automáticas ("Arich", "Ariz", ...)
        Assert.False(string.IsNullOrWhiteSpace(vm.NewWordAliasesText));

        await vm.AddOrUpdateTermAsync();

        // Entradas deben quedar limpias
        Assert.Equal(string.Empty, vm.NewWord);
        Assert.Equal(string.Empty, vm.NewWordAliasesText);

        // Término añadido a la lista
        Assert.Single(vm.Terms);
        Assert.Equal("Aritz", vm.Terms[0].Word);
        Assert.Contains("Arich", vm.Terms[0].Aliases);

        // Total count actualizado
        Assert.Equal(1, vm.TotalCount);
        Assert.Single(vm.FilteredTerms);
    }

    [Fact]
    public async Task AddOrUpdateTermCommand_ExecutesAndSavesWordWithAliases()
    {
        var service = new VocabularyService(_tempJsonFile);
        var vm = new VocabularyManagerViewModel(service);

        vm.NewWord = "Aritz";
        Assert.False(string.IsNullOrWhiteSpace(vm.NewWordAliasesText));

        Assert.True(vm.AddOrUpdateTermCommand.CanExecute(null));
        await vm.AddOrUpdateTermCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.NewWord);
        Assert.Equal(string.Empty, vm.NewWordAliasesText);
        Assert.Single(vm.Terms);
        Assert.Equal("Aritz", vm.Terms[0].Word);
    }

    [Fact]
    public async Task EditTerm_LoadsDataIntoInputsForEditing()
    {
        var service = new VocabularyService(_tempJsonFile);
        service.AddOrUpdateTerm("Aritz", new[] { "Arich", "Ariz" });
        await service.SaveAsync();

        var vm = new VocabularyManagerViewModel(service);
        var item = vm.Terms.FirstOrDefault(t => t.Word == "Aritz");
        Assert.NotNull(item);

        vm.EditTerm(item);

        Assert.Equal("Aritz", vm.NewWord);
        Assert.Contains("Arich", vm.NewWordAliasesText);
    }

    [Fact]
    public async Task RemoveTermAsync_RemovesFromCollectionAndService()
    {
        var service = new VocabularyService(_tempJsonFile);
        service.AddOrUpdateTerm("Aritz", new[] { "Arich" });
        await service.SaveAsync();

        var vm = new VocabularyManagerViewModel(service);
        Assert.Single(vm.Terms);

        var item = vm.Terms[0];
        await vm.RemoveTermAsync(item);

        Assert.Empty(vm.Terms);
        Assert.Empty(vm.FilteredTerms);
        Assert.Equal(0, vm.TotalCount);
        Assert.Empty(service.GetTerms());
    }

    [Fact]
    public void SearchQuery_FiltersTermsByWordOrAlias()
    {
        var service = new VocabularyService(_tempJsonFile);
        service.AddOrUpdateTerm("Aritz", new[] { "Arich" });

        var vm = new VocabularyManagerViewModel(service);
        Assert.Single(vm.FilteredTerms);

        // Filtrar por palabra
        vm.SearchQuery = "ari";
        Assert.Single(vm.FilteredTerms);
        Assert.Equal("Aritz", vm.FilteredTerms[0].Word);

        // Filtrar por alias
        vm.SearchQuery = "arich";
        Assert.Single(vm.FilteredTerms);
        Assert.Equal("Aritz", vm.FilteredTerms[0].Word);

        // Sin coincidencia
        vm.SearchQuery = "inexistente";
        Assert.Empty(vm.FilteredTerms);

        // Limpiar filtro
        vm.SearchQuery = "";
        Assert.Single(vm.FilteredTerms);
    }
}
