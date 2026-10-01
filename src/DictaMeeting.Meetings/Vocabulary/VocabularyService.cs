using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace DictaMeeting.Meetings.Vocabulary;

/// <summary>
/// Servicio central para la gestión, persistencia, importación/exportación y aplicación
/// de vocabulario oficial y sustitución fonética de alias.
/// </summary>
public class VocabularyService : IVocabularyService
{
    private readonly string _storageFilePath;
    private readonly ILogger<VocabularyService>? _logger;
    private readonly object _lock = new();
    private readonly List<VocabularyTerm> _terms = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public event EventHandler? VocabularyChanged;

    public VocabularyService(string? customFilePath = null, ILogger<VocabularyService>? logger = null)
    {
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(customFilePath))
        {
            _storageFilePath = customFilePath;
        }
        else
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var folder = Path.Combine(localAppData, "DictaMeeting");
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            _storageFilePath = Path.Combine(folder, "vocabulary.json");
        }
    }

    public IReadOnlyList<VocabularyTerm> GetTerms()
    {
        lock (_lock)
        {
            return _terms.Select(CloneTerm).ToList();
        }
    }

    public IReadOnlyList<string> GetOfficialWords()
    {
        lock (_lock)
        {
            return _terms.Select(t => t.Word).Where(w => !string.IsNullOrWhiteSpace(w)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public void AddOrUpdateTerm(string word, IEnumerable<string>? aliases = null)
    {
        if (string.IsNullOrWhiteSpace(word)) return;

        var cleanWord = word.Trim();
        var cleanAliases = aliases?
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .Where(a => !string.Equals(a, cleanWord, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

        lock (_lock)
        {
            var existing = _terms.FirstOrDefault(t => string.Equals(t.Word, cleanWord, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Word = cleanWord;
                existing.Aliases = cleanAliases;
            }
            else
            {
                _terms.Add(new VocabularyTerm
                {
                    Word = cleanWord,
                    Aliases = cleanAliases
                });
            }
        }

        OnVocabularyChanged();
    }

    public bool RemoveTerm(string wordOrId)
    {
        if (string.IsNullOrWhiteSpace(wordOrId)) return false;

        bool removed;
        lock (_lock)
        {
            int index = _terms.FindIndex(t =>
                string.Equals(t.Id, wordOrId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Word, wordOrId, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                _terms.RemoveAt(index);
                removed = true;
            }
            else
            {
                removed = false;
            }
        }

        if (removed)
        {
            OnVocabularyChanged();
        }

        return removed;
    }

    public void ClearAll()
    {
        lock (_lock)
        {
            _terms.Clear();
        }
        OnVocabularyChanged();
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_storageFilePath))
            {
                return;
            }

            var json = await File.ReadAllTextAsync(_storageFilePath, cancellationToken);
            if (string.IsNullOrWhiteSpace(json)) return;

            var data = JsonSerializer.Deserialize<VocabularyData>(json, JsonOptions);
            if (data?.Terms != null)
            {
                lock (_lock)
                {
                    _terms.Clear();
                    foreach (var term in data.Terms.Where(t => !string.IsNullOrWhiteSpace(t.Word)))
                    {
                        term.Word = term.Word.Trim();
                        term.Aliases = term.Aliases?
                            .Where(a => !string.IsNullOrWhiteSpace(a))
                            .Select(a => a.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList() ?? new List<string>();
                        _terms.Add(term);
                    }
                }
                _logger?.LogInformation("Vocabulario cargado con éxito desde '{Path}'. Total términos: {Count}", _storageFilePath, _terms.Count);
                OnVocabularyChanged();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error al cargar vocabulario desde '{Path}'. Se iniciará vacío.", _storageFilePath);
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var dir = Path.GetDirectoryName(_storageFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            VocabularyData data;
            lock (_lock)
            {
                data = new VocabularyData
                {
                    Version = 1,
                    Terms = _terms.Select(CloneTerm).ToList()
                };
            }

            var json = JsonSerializer.Serialize(data, JsonOptions);
            await File.WriteAllTextAsync(_storageFilePath, json, cancellationToken);
            _logger?.LogDebug("Vocabulario guardado con éxito en '{Path}'.", _storageFilePath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error al guardar vocabulario en '{Path}'.", _storageFilePath);
            throw;
        }
    }

    public string FormatWhisperGlossaryPrompt()
    {
        List<string> words;
        lock (_lock)
        {
            words = _terms
                .Select(t => t.Word.Trim())
                .Where(w => !string.IsNullOrWhiteSpace(w))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (words.Count == 0) return string.Empty;

        return "Glosario: " + string.Join(", ", words) + ".";
    }

    public string FormatSherpaHotwords(int maxTerms = 20)
    {
        List<string> words;
        lock (_lock)
        {
            var query = _terms
                .Select(t => t.Word.Trim())
                .Where(w => !string.IsNullOrWhiteSpace(w))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            if (maxTerms > 0)
            {
                query = query.Take(maxTerms);
            }

            words = query.ToList();
        }

        if (words.Count == 0) return string.Empty;

        return string.Join(", ", words);
    }

    public string ReplaceAliases(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        List<(string Alias, string Replacement)> replacements;
        lock (_lock)
        {
            replacements = _terms
                .SelectMany(t => t.Aliases.Select(a => (Alias: a, Replacement: t.Word)))
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Alias) &&
                               !string.Equals(pair.Alias, pair.Replacement, StringComparison.OrdinalIgnoreCase))
                // Ordenar de mayor a menor longitud para evitar sustituciones parciales de prefijos
                .OrderByDescending(pair => pair.Alias.Length)
                .ToList();
        }

        if (replacements.Count == 0) return text;

        string result = text;
        foreach (var (alias, replacement) in replacements)
        {
            // Usar límites de palabra de forma segura respetando caracteres alfanuméricos y acentos
            string pattern = $@"(?<!\p{{L}}){Regex.Escape(alias)}(?!\p{{L}})";
            result = Regex.Replace(result, pattern, replacement, RegexOptions.IgnoreCase);
        }

        return result;
    }

    public async Task ExportToFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        VocabularyData data;
        lock (_lock)
        {
            data = new VocabularyData
            {
                Version = 1,
                Terms = _terms.Select(CloneTerm).ToList()
            };
        }

        var json = JsonSerializer.Serialize(data, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    public async Task ImportFromFileAsync(string filePath, bool merge = true, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"No se encontró el archivo a importar: {filePath}");
        }

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var data = JsonSerializer.Deserialize<VocabularyData>(json, JsonOptions);
        if (data?.Terms == null)
        {
            throw new InvalidDataException("El archivo JSON no contiene una estructura válida de vocabulario.");
        }

        lock (_lock)
        {
            if (!merge)
            {
                _terms.Clear();
            }

            foreach (var incoming in data.Terms.Where(t => !string.IsNullOrWhiteSpace(t.Word)))
            {
                var cleanWord = incoming.Word.Trim();
                var incomingAliases = incoming.Aliases?
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .Select(a => a.Trim())
                    .Where(a => !string.Equals(a, cleanWord, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? new List<string>();

                var existing = _terms.FirstOrDefault(t => string.Equals(t.Word, cleanWord, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    // Combinar alias existentes con los nuevos
                    var union = new HashSet<string>(existing.Aliases, StringComparer.OrdinalIgnoreCase);
                    foreach (var a in incomingAliases)
                    {
                        union.Add(a);
                    }
                    existing.Aliases = union.ToList();
                }
                else
                {
                    _terms.Add(new VocabularyTerm
                    {
                        Word = cleanWord,
                        Aliases = incomingAliases
                    });
                }
            }
        }

        await SaveAsync(cancellationToken);
        OnVocabularyChanged();
    }

    private void OnVocabularyChanged()
    {
        VocabularyChanged?.Invoke(this, EventArgs.Empty);
    }

    private static VocabularyTerm CloneTerm(VocabularyTerm source)
    {
        return new VocabularyTerm
        {
            Id = source.Id,
            Word = source.Word,
            Aliases = source.Aliases != null ? new List<string>(source.Aliases) : new List<string>(),
            CreatedAt = source.CreatedAt
        };
    }
}
