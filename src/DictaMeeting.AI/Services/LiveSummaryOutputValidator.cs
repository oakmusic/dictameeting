using System.Text.RegularExpressions;
using DictaMeeting.AI.Interfaces;

namespace DictaMeeting.AI.Services;

/// <summary>
/// Validador de salida del modelo local de resumen en vivo.
/// Asegura que ninguna tarjeta corrupta, en bucle degenerativo o inconexa llegue a la interfaz de usuario.
/// </summary>
public class LiveSummaryOutputValidator : ILiveSummaryOutputValidator
{
    private static readonly Regex SentenceSplitter = new(
        @"(?<=[.!?])\s+|\r?\n+",
        RegexOptions.Compiled);

    private static readonly Regex PunctuationCleaner = new(
        @"[^\p{L}\p{N}\s]",
        RegexOptions.Compiled);

    private static readonly HashSet<string> CommonStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Español
        "el", "la", "los", "las", "un", "una", "unos", "unas", "de", "del", "en", "para", "por",
        "con", "se", "ha", "han", "que", "y", "o", "a", "al", "su", "sus", "este", "esta", "estos",
        "estas", "pero", "cuando", "donde", "porque", "sobre", "entre", "desde", "hasta", "hacia",
        "todo", "toda", "todos", "todas", "otro", "otra", "otros", "otras", "tanto", "tanta", "mucho",
        "mucha", "muchos", "muchas", "poco", "poca", "cada", "mismo", "misma", "tambien", "también",
        "solo", "sólo", "ellos", "ellas", "nosotros", "ustedes", "haber", "habia", "había", "tiene",
        "tienen", "tener", "hacer", "hecho", "sido", "están", "estamos", "esto", "esos", "esas",
        "aquel", "aquello", "quienes", "quien", "cual", "cuales", "cuál", "cuáles", "comentado",
        "explicado", "acordado", "indicado", "expuesto", "reunión", "tramo", "sesión", "sesion",
        "punto", "tema", "parte",

        // Inglés
        "the", "and", "that", "this", "with", "for", "from", "have", "they", "what", "your",
        "were", "some", "then", "there", "their", "will", "would", "could", "should", "just",
        "very", "about", "into", "when", "more", "also", "been", "each", "which", "time",
        "them", "than", "only", "other", "discussed", "explained", "mentioned", "meeting",
        "section", "today", "here"
    };

    private static readonly string[] PreamblePrefixes =
    {
        "se ha comentado que ",
        "se ha explicado que ",
        "se ha acordado que ",
        "se ha mencionado que ",
        "se ha indicado que ",
        "se ha expuesto que ",
        "se comento que ",
        "se comentó que ",
        "se explico que ",
        "se explicó que ",
        "it was discussed that ",
        "it was explained that ",
        "it was agreed that ",
        "it was mentioned that ",
        "the speaker explained that "
    };

    public LiveSummaryValidationResult ValidateOutput(
        string generatedSummary,
        string inputTranscript,
        string? previousCardText,
        TimeSpan cardStartTime,
        TimeSpan cardEndTime,
        TimeSpan lastSummarizedEndTime)
    {
        // 1. Validación de rango temporal
        if (cardEndTime <= cardStartTime)
        {
            return LiveSummaryValidationResult.Failure(
                LiveSummaryValidationFailureReason.InvalidTimeRange,
                $"El tiempo final ({cardEndTime}) debe ser mayor al inicial ({cardStartTime}).");
        }

        if (cardEndTime <= lastSummarizedEndTime)
        {
            return LiveSummaryValidationResult.Failure(
                LiveSummaryValidationFailureReason.InvalidTimeRange,
                $"El tramo ({cardStartTime} - {cardEndTime}) ya fue previamente resumido (último fin: {lastSummarizedEndTime}).");
        }

        // 2. Validación de longitud mínima
        if (string.IsNullOrWhiteSpace(generatedSummary) || generatedSummary.Trim().Length < 12)
        {
            return LiveSummaryValidationResult.Failure(
                LiveSummaryValidationFailureReason.EmptyOrTooShort,
                "El resumen generado está vacío o es demasiado corto (< 12 caracteres).");
        }

        var words = ExtractWords(generatedSummary);
        if (words.Length < 4)
        {
            return LiveSummaryValidationResult.Failure(
                LiveSummaryValidationFailureReason.EmptyOrTooShort,
                $"El resumen generado sólo contiene {words.Length} palabras (mínimo 4).");
        }


        // 4. Detección de repetición de frases / oraciones
        var sentences = SplitIntoSentences(generatedSummary);
        var normalizedSentenceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var rawSentenceMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawSentence in sentences)
        {
            var normalized = NormalizeSentence(rawSentence);
            if (string.IsNullOrWhiteSpace(normalized) || normalized.Length < 10) continue;

            // Ignorar oraciones excesivamente cortas como "Hola." o "De acuerdo."
            var sentenceWords = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (sentenceWords.Length < 3) continue;

            if (!normalizedSentenceCounts.ContainsKey(normalized))
            {
                normalizedSentenceCounts[normalized] = 0;
                rawSentenceMap[normalized] = rawSentence;
            }
            normalizedSentenceCounts[normalized]++;

            if (normalizedSentenceCounts[normalized] >= 2)
            {
                return LiveSummaryValidationResult.Failure(
                    LiveSummaryValidationFailureReason.RepeatedSentence,
                    $"Frase repetida múltiples veces ({normalizedSentenceCounts[normalized]} veces): \"{rawSentenceMap[normalized]}\".");
            }
        }

        // 5. Detección de bucles degenerativos de N-gramas (3-gramas o 4-gramas que se repiten en loop)
        var ngramCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i <= words.Length - 4; i++)
        {
            var ngram = $"{words[i]} {words[i + 1]} {words[i + 2]} {words[i + 3]}";
            ngramCounts[ngram] = ngramCounts.GetValueOrDefault(ngram) + 1;
            if (ngramCounts[ngram] >= 3)
            {
                return LiveSummaryValidationResult.Failure(
                    LiveSummaryValidationFailureReason.DegenerativeLoop,
                    $"Bucle degenerativo de 4-grama detectado ({ngramCounts[ngram]} repeticiones): \"{ngram}\".");
            }
        }

        // Detección de repeticiones consecutivas de 2 palabras (ej. "palabra1 palabra2 palabra1 palabra2 palabra1 palabra2")
        for (int i = 0; i <= words.Length - 6; i += 2)
        {
            if (string.Equals(words[i], words[i + 2], StringComparison.OrdinalIgnoreCase) &&
                string.Equals(words[i + 1], words[i + 3], StringComparison.OrdinalIgnoreCase) &&
                string.Equals(words[i], words[i + 4], StringComparison.OrdinalIgnoreCase) &&
                string.Equals(words[i + 1], words[i + 5], StringComparison.OrdinalIgnoreCase))
            {
                return LiveSummaryValidationResult.Failure(
                    LiveSummaryValidationFailureReason.DegenerativeLoop,
                    $"Secuencia repetitiva consecutiva detectada: \"{words[i]} {words[i + 1]}\".");
            }
        }

        // 6. Dominancia de una misma oración (si una sola oración/núcleo forma más del 40% del texto total)
        if (words.Length >= 25 && sentences.Length >= 2)
        {
            foreach (var sentence in sentences)
            {
                var sWords = ExtractWords(sentence);
                if (sWords.Length >= 6)
                {
                    // Buscar si hay otra oración con similitud > 75%
                    int similarCount = 0;
                    foreach (var other in sentences)
                    {
                        if (ReferenceEquals(sentence, other)) continue;
                        var oWords = ExtractWords(other);
                        if (oWords.Length >= 6 && CalculateWordOverlapSimilarity(sWords, oWords) >= 0.70)
                        {
                            similarCount++;
                        }
                    }

                    if (similarCount >= 1 && (sWords.Length * (similarCount + 1)) >= words.Length * 0.40)
                    {
                        return LiveSummaryValidationResult.Failure(
                            LiveSummaryValidationFailureReason.SentenceDominance,
                            $"La misma estructura de oración domina más del 40% del texto generado.");
                    }
                }
            }
        }

        // 6. Validación de longitud excesiva (una tarjeta debe rondar ~50 palabras, máx ~95)
        if (words.Length > 95 || generatedSummary.Length > 650)
        {
            return LiveSummaryValidationResult.Failure(
                LiveSummaryValidationFailureReason.ExcessiveLength,
                $"El resumen es excesivamente largo ({words.Length} palabras, {generatedSummary.Length} caracteres). Máximo permitido: 95 palabras.");
        }

        // 7. Comparación con la tarjeta anterior (detección de duplicado / sin avance)
        if (!string.IsNullOrWhiteSpace(previousCardText))
        {
            var prevWords = ExtractWords(previousCardText);
            if (prevWords.Length >= 8)
            {
                double similarity = CalculateWordOverlapSimilarity(words, prevWords);
                if (similarity >= 0.82)
                {
                    return LiveSummaryValidationResult.Failure(
                        LiveSummaryValidationFailureReason.DuplicateOfPreviousCard,
                        $"El resumen generado es prácticamente idéntico al de la tarjeta anterior (similitud: {similarity:P0}).");
                }
            }
        }

        // 8. Relación semántica mínima con la transcripción de entrada
        if (!string.IsNullOrWhiteSpace(inputTranscript))
        {
            var transcriptContentWords = ExtractContentWords(inputTranscript);
            var summaryContentWords = ExtractContentWords(generatedSummary);

            if (transcriptContentWords.Count >= 5 && summaryContentWords.Count >= 5)
            {
                int commonCount = CountMatchingContentWords(summaryContentWords, transcriptContentWords);
                if (commonCount == 0)
                {
                    return LiveSummaryValidationResult.Failure(
                        LiveSummaryValidationFailureReason.SemanticDisconnect,
                        "El resumen generado no contiene palabras de contenido ni términos en común con la transcripción de entrada (desconexión o alucinación).");
                }
            }
        }

        return LiveSummaryValidationResult.Success();
    }

    private static string[] SplitIntoSentences(string text)
    {
        return SentenceSplitter.Split(text)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();
    }

    private static string NormalizeSentence(string sentence)
    {
        var clean = sentence.Trim().ToLowerInvariant();
        clean = PunctuationCleaner.Replace(clean, " ");
        clean = Regex.Replace(clean, @"\s+", " ").Trim();

        // Eliminar prefijos formulaicos impersonales
        foreach (var prefix in PreamblePrefixes)
        {
            if (clean.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                clean = clean.Substring(prefix.Length).Trim();
                break;
            }
        }

        return clean;
    }

    private static string[] ExtractWords(string text)
    {
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    private static HashSet<string> ExtractContentWords(string text)
    {
        var clean = PunctuationCleaner.Replace(text.ToLowerInvariant(), " ");
        var tokens = clean.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tokens)
        {
            if (t.Length >= 4 && !CommonStopWords.Contains(t))
            {
                result.Add(t);
            }
        }
        return result;
    }

    private static int CountMatchingContentWords(HashSet<string> summaryWords, HashSet<string> transcriptWords)
    {
        int matches = 0;
        foreach (var sw in summaryWords)
        {
            if (transcriptWords.Contains(sw))
            {
                matches++;
                continue;
            }

            // Comprobación de raíces comunes para soporte bilingüe (ej. "interrup" -> "interrupciones" / "interruptions")
            if (sw.Length >= 5)
            {
                var stem = sw.Substring(0, Math.Min(5, sw.Length));
                if (transcriptWords.Any(tw => tw.StartsWith(stem, StringComparison.OrdinalIgnoreCase)))
                {
                    matches++;
                }
            }
        }
        return matches;
    }

    private static double CalculateWordOverlapSimilarity(string[] wordsA, string[] wordsB)
    {
        var setA = wordsA.Select(w => PunctuationCleaner.Replace(w.ToLowerInvariant(), "").Trim())
                         .Where(w => w.Length >= 3 && !CommonStopWords.Contains(w))
                         .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var setB = wordsB.Select(w => PunctuationCleaner.Replace(w.ToLowerInvariant(), "").Trim())
                         .Where(w => w.Length >= 3 && !CommonStopWords.Contains(w))
                         .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (setA.Count == 0 || setB.Count == 0) return 0.0;

        int intersection = setA.Intersect(setB).Count();
        int union = setA.Union(setB).Count();

        return union > 0 ? (double)intersection / union : 0.0;
    }
}
