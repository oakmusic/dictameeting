using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DictaMeeting.Meetings.Vocabulary;
using Microsoft.Win32;

namespace DictaMeeting.App.ViewModels;

/// <summary>
/// ViewModel que gestiona la ventana/modal de administración de vocabulario,
/// palabras clave, sugerencias fonéticas automáticas e importación/exportación.
/// </summary>
public partial class VocabularyManagerViewModel : ObservableObject
{
    private readonly IVocabularyService _vocabularyService;

    [ObservableProperty]
    private string _newWord = string.Empty;

    [ObservableProperty]
    private string _newWordAliasesText = string.Empty;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasStatusMessage;

    [ObservableProperty]
    private int _totalCount;

    public ObservableCollection<VocabularyItemViewModel> Terms { get; } = new();
    public ObservableCollection<VocabularyItemViewModel> FilteredTerms { get; } = new();

    public VocabularyManagerViewModel(IVocabularyService vocabularyService)
    {
        _vocabularyService = vocabularyService;
        _vocabularyService.VocabularyChanged += (s, e) => RefreshTerms();
        RefreshTerms();
    }

    /// <summary>
    /// Al cambiar la palabra oficial en el campo de texto, genera automáticamente sugerencias fonéticas
    /// realistas para euskera, castellano y acrónimos sin requerir que el usuario pulse ningún botón.
    /// </summary>
    partial void OnNewWordChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            NewWordAliasesText = string.Empty;
            return;
        }

        var suggestions = PhoneticRuleEngine.GenerateSuggestedAliases(value);
        if (suggestions.Count > 0)
        {
            NewWordAliasesText = string.Join(", ", suggestions);
        }
        else
        {
            NewWordAliasesText = string.Empty;
        }
    }

    partial void OnSearchQueryChanged(string value)
    {
        ApplyFilter();
    }

    [RelayCommand]
    public async Task AddOrUpdateTermAsync()
    {
        if (string.IsNullOrWhiteSpace(NewWord))
        {
            SetStatus("Por favor, introduzca una palabra o acrónimo.");
            return;
        }

        var cleanWord = NewWord.Trim();
        var aliases = ParseAliases(NewWordAliasesText);

        _vocabularyService.AddOrUpdateTerm(cleanWord, aliases);
        await _vocabularyService.SaveAsync();

        NewWord = string.Empty;
        NewWordAliasesText = string.Empty;

        SetStatus($"'{cleanWord}' guardado con éxito.");
    }

    [RelayCommand]
    public async Task RemoveTermAsync(VocabularyItemViewModel? item)
    {
        if (item == null) return;

        bool removed = _vocabularyService.RemoveTerm(item.Word);
        if (removed)
        {
            await _vocabularyService.SaveAsync();
            SetStatus($"'{item.Word}' eliminado.");
        }
    }

    [RelayCommand]
    public void EditTerm(VocabularyItemViewModel? item)
    {
        if (item == null) return;

        NewWord = item.Word;
        NewWordAliasesText = string.Join(", ", item.Aliases);
    }

    [RelayCommand]
    public async Task ExportToFileAsync()
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Title = "Exportar Vocabulario DictaMeeting",
                Filter = "Archivo JSON (*.json)|*.json",
                FileName = $"dictameeting_vocabulario_{DateTime.Now:yyyyMMdd}.json"
            };

            if (dialog.ShowDialog() == true)
            {
                await _vocabularyService.ExportToFileAsync(dialog.FileName);
                SetStatus($"Vocabulario exportado con éxito a '{System.IO.Path.GetFileName(dialog.FileName)}'.");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Error al exportar: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task ImportFromFileAsync()
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Importar Vocabulario a DictaMeeting",
                Filter = "Archivo JSON (*.json)|*.json"
            };

            if (dialog.ShowDialog() == true)
            {
                await _vocabularyService.ImportFromFileAsync(dialog.FileName, merge: true);
                SetStatus($"Vocabulario importado con éxito desde '{System.IO.Path.GetFileName(dialog.FileName)}'.");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Error al importar: {ex.Message}");
        }
    }

    public void RefreshTerms()
    {
        var terms = _vocabularyService.GetTerms();
        Terms.Clear();
        foreach (var t in terms.OrderBy(t => t.Word, StringComparer.OrdinalIgnoreCase))
        {
            Terms.Add(VocabularyItemViewModel.FromModel(t));
        }

        TotalCount = Terms.Count;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredTerms.Clear();
        var query = SearchQuery?.Trim();

        var matches = string.IsNullOrWhiteSpace(query)
            ? Terms
            : Terms.Where(t =>
                t.Word.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Aliases.Any(a => a.Contains(query, StringComparison.OrdinalIgnoreCase)));

        foreach (var item in matches)
        {
            FilteredTerms.Add(item);
        }
    }

    private static List<string> ParseAliases(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();

        return text.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(a => a.Trim())
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void SetStatus(string message)
    {
        StatusMessage = message;
        HasStatusMessage = !string.IsNullOrWhiteSpace(message);
    }
}
