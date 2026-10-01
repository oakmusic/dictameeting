using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DictaMeeting.Meetings.Vocabulary;

/// <summary>
/// Motor algorítmico determinista de reglas fonéticas para el entorno Euskera / Castellano / Acrónimos.
/// Genera instantáneamente sugerencias de transcripción fonética que los modelos ASR (como Whisper)
/// suelen cometer al transcribir nombres propios o términos técnicos.
/// </summary>
public static class PhoneticRuleEngine
{
    /// <summary>
    /// Genera sugerencias fonéticas realistas para una palabra o acrónimo dado.
    /// </summary>
    public static IReadOnlyList<string> GenerateSuggestedAliases(string? word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return Array.Empty<string>();
        }

        var trimmed = word.Trim();
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool isAllCaps = trimmed.Length > 1 && trimmed.All(c => !char.IsLetter(c) || char.IsUpper(c));
        string baseLower = trimmed.ToLowerInvariant();

        // 1. Acrónimos en MAYÚSCULAS: Whisper casi siempre transcribe en minúsculas o formato Título
        if (isAllCaps)
        {
            results.Add(baseLower);
            results.Add(ToTitleCase(baseLower));
        }

        // 2. Reglas de transformación fonética combinatorias sobre la versión en minúsculas
        var variations = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { baseLower };

        // 2.1 Euskera: 'tz' -> 'ch', 'z', 's' (ej. Aritz -> Arich, Ariz, Aris)
        if (baseLower.Contains("tz"))
        {
            var nextSet = new HashSet<string>(variations, StringComparer.OrdinalIgnoreCase);
            foreach (var v in variations)
            {
                nextSet.Add(v.Replace("tz", "ch"));
                nextSet.Add(v.Replace("tz", "z"));
                nextSet.Add(v.Replace("tz", "s"));
            }
            variations = nextSet;
        }

        // 2.2 Euskera: 'ts' -> 'ch', 's', 'tx'
        if (baseLower.Contains("ts"))
        {
            var nextSet = new HashSet<string>(variations, StringComparer.OrdinalIgnoreCase);
            foreach (var v in variations)
            {
                nextSet.Add(v.Replace("ts", "ch"));
                nextSet.Add(v.Replace("ts", "s"));
                nextSet.Add(v.Replace("ts", "tx"));
            }
            variations = nextSet;
        }

        // 2.3 Euskera: 'tx' -> 'ch'
        if (baseLower.Contains("tx"))
        {
            var nextSet = new HashSet<string>(variations, StringComparer.OrdinalIgnoreCase);
            foreach (var v in variations)
            {
                nextSet.Add(v.Replace("tx", "ch"));
            }
            variations = nextSet;
        }

        // 2.4 Euskera: 'x' -> 'ch', 's'
        if (baseLower.Contains("x"))
        {
            var nextSet = new HashSet<string>(variations, StringComparer.OrdinalIgnoreCase);
            foreach (var v in variations)
            {
                nextSet.Add(v.Replace("x", "ch"));
                nextSet.Add(v.Replace("x", "s"));
            }
            variations = nextSet;
        }

        // 2.5 H muda (inicial o intermedia): omitida o añadida (ej. Haritz -> Aritz / Aritz -> Haritz)
        if (baseLower.StartsWith("h"))
        {
            var nextSet = new HashSet<string>(variations, StringComparer.OrdinalIgnoreCase);
            foreach (var v in variations)
            {
                if (v.StartsWith("h"))
                {
                    nextSet.Add(v.Substring(1));
                }
            }
            variations = nextSet;
        }
        else if (char.IsLetter(baseLower[0]))
        {
            // Ocasionalmente Whisper añade 'h' a nombres vascos como Aritz -> Haritz
            results.Add("H" + baseLower.Substring(1));
            results.Add("h" + baseLower.Substring(1));
        }

        // 2.6 'k' -> 'c' (o 'qu' ante e, i)
        if (baseLower.Contains("k"))
        {
            var nextSet = new HashSet<string>(variations, StringComparer.OrdinalIgnoreCase);
            foreach (var v in variations)
            {
                // k final o antes de a, o, u o consonante -> 'c'
                string cReplaced = Regex.Replace(v, "k(?=[aou\\s]|$)", "c");
                // k antes de e, i -> 'qu' o 'c'
                cReplaced = Regex.Replace(cReplaced, "k(?=[ei])", "qu");
                nextSet.Add(cReplaced);

                // Alternativa común en transcripciones: reemplazar cualquier k por c
                nextSet.Add(v.Replace("k", "c"));
            }
            variations = nextSet;
        }

        // 2.7 'z' -> 's' en español (ej. Aritz -> Aris)
        if (baseLower.Contains("z"))
        {
            var nextSet = new HashSet<string>(variations, StringComparer.OrdinalIgnoreCase);
            foreach (var v in variations)
            {
                nextSet.Add(v.Replace("z", "s"));
            }
            variations = nextSet;
        }

        // 3. Formatear y añadir variaciones encontradas respetando el casing original
        bool isTitle = !isAllCaps && trimmed.Length > 0 && char.IsUpper(trimmed[0]);

        foreach (var v in variations)
        {
            if (string.Equals(v, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (isTitle)
            {
                results.Add(ToTitleCase(v));
                results.Add(v.ToLowerInvariant());
            }
            else if (isAllCaps)
            {
                results.Add(v.ToLowerInvariant());
                results.Add(ToTitleCase(v));
            }
            else
            {
                results.Add(v);
            }
        }

        // Eliminar la propia palabra original
        results.Remove(trimmed);
        results.Remove(baseLower);

        // Limitar a las 6 sugerencias más relevantes y ordenadas
        return results
            .Where(r => !string.IsNullOrWhiteSpace(r) && r.Length >= 2)
            .Take(6)
            .ToList();
    }

    private static string ToTitleCase(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (text.Length == 1) return text.ToUpperInvariant();
        return char.ToUpperInvariant(text[0]) + text.Substring(1).ToLowerInvariant();
    }
}
