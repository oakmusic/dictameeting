using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DictaMeeting.Transcription.Models;

namespace DictaMeeting.Transcription.Services;

/// <summary>
/// Tipo de anomalía detectada en el resultado de transcripción ASR.
/// </summary>
public enum AsrAnomalyType
{
    None,
    SpeechRateExceeded,
    NgramRepetitionLoop,
    VocabularyEchoing,
    ZeroDurationOrEmpty
}

/// <summary>
/// Resultado del análisis de anomalías acústicas o lingüísticas en un segmento de ASR.
/// </summary>
public record AsrAnomalyResult(
    bool IsAnomalous,
    AsrAnomalyType AnomalyType,
    string? Reason,
    double MetricValue = 0.0);

/// <summary>
/// Detector robusto y conservador de alucinaciones y bucles degenerados en modelos ASR (ej. Qwen3-ASR / Whisper).
/// Diseñado para detectar:
/// 1. Tasa de palabras por segundo físicamente imposible (alucinaciones en blips de audio cortos o casi silenciosos).
/// 2. Bucles de repetición degenerados de n-gramas (bucles autoregresivos musicales o de etiquetas como "Bass Guitar...").
/// 3. Volcado literal de vocabulario/hotwords sin evidencia acústica ni conectores gramaticales.
/// 
/// Conservador: Permite repeticiones conversacionales legítimas ("sí, sí", "no, no, no", "vale, vale").
/// </summary>
public static class AsrAnomalyDetector
{
    private static readonly char[] WordSeparators = new[] { ' ', '\t', '\r', '\n', ',', '.', ';', ':', '!', '?', '(', ')', '[', ']', '"', '«', '»' };

    /// <summary>
    /// Conjunto de conectores gramaticales comunes en español y en inglés para discernir habla real vs. lista cruda de términos.
    /// </summary>
    private static readonly HashSet<string> FunctionalWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Español
        "de", "la", "el", "en", "y", "a", "los", "del", "se", "las", "por", "un", "para", "con", "no", "una",
        "su", "al", "lo", "como", "más", "pero", "sus", "le", "ya", "o", "este", "sí", "porque", "esta",
        "son", "entre", "está", "cuando", "muy", "sin", "sobre", "también", "me", "hasta", "hay", "donde",
        "quien", "desde", "todo", "nos", "durante", "todos", "uno", "les", "ni", "contra", "otros", "ese",
        "eso", "ante", "ellos", "e", "esto", "mí", "antes", "algunos", "qué", "unos", "yo", "otro", "otras",
        "otra", "él", "tanto", "esa", "estos", "mucho", "quienes", "nada", "muchos", "cual", "poco", "ella",
        "estar", "estas", "algunas", "algo", "nosotros", "mi", "mis", "tú", "te", "ti", "tu", "tus", "ellas",
        "nosotras", "vosostros", "vosotras", "es", "fue", "ha", "han", "hemos", "he", "son", "sea", "que",
        // Inglés
        "the", "be", "to", "of", "and", "a", "in", "that", "have", "i", "it", "for", "not", "on", "with",
        "he", "as", "you", "do", "at", "this", "but", "his", "by", "from", "they", "we", "say", "her", "she",
        "or", "an", "will", "my", "one", "all", "would", "there", "their", "what", "so", "up", "out", "if",
        "about", "who", "get", "which", "go", "me", "when", "make", "can", "like", "time", "no", "just", "him"
    };

    /// <summary>
    /// Analiza exhaustivamente un segmento transcrito para verificar si es plausible o anómalo.
    /// </summary>
    public static AsrAnomalyResult DetectAnomaly(
        string? text,
        TimeSpan duration,
        IReadOnlyList<string>? vocabularyTerms = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new AsrAnomalyResult(false, AsrAnomalyType.None, null);
        }

        var words = ExtractWords(text);
        if (words.Count == 0)
        {
            return new AsrAnomalyResult(false, AsrAnomalyType.None, null);
        }

        // 1. Verificación de tasa de palabras por segundo (Speech Rate)
        var rateResult = CheckSpeechRate(words, duration);
        if (rateResult.IsAnomalous)
        {
            return rateResult;
        }

        // 2. Verificación de repetición patológica de n-gramas (bucles autoregresivos)
        var repResult = CheckNgramRepetition(words);
        if (repResult.IsAnomalous)
        {
            return repResult;
        }

        // 3. Verificación de volcado o eco de vocabulario sin base acústica
        if (vocabularyTerms != null && vocabularyTerms.Count > 0)
        {
            var vocabResult = CheckVocabularyEchoing(words, vocabularyTerms);
            if (vocabResult.IsAnomalous)
            {
                return vocabResult;
            }
        }

        return new AsrAnomalyResult(false, AsrAnomalyType.None, null);
    }

    /// <summary>
    /// Detecta tasas de palabras físicamente incompatibles con la fisiología del habla humana.
    /// El habla conversacional típica es de 2 a 3.5 palabras/segundo.
    /// Un locutor extremadamente veloz (subastador / rapero profesional) rara vez supera 6-7 palabras/segundo.
    /// </summary>
    public static AsrAnomalyResult CheckSpeechRate(IReadOnlyList<string> words, TimeSpan duration)
    {
        double seconds = duration.TotalSeconds;

        // Caso A: Segmento extremadamente corto (< 0.5s) con múltiples palabras
        if (seconds < 0.50 && words.Count >= 3)
        {
            double rate = words.Count / Math.Max(0.05, seconds);
            return new AsrAnomalyResult(
                true,
                AsrAnomalyType.SpeechRateExceeded,
                $"Tasa de habla imposible: {words.Count} palabras en {seconds:F3}s ({rate:F1} palabras/segundo).",
                rate);
        }

        // Caso B: Segmento sub-segundo (< 1.0s) con más de 5 palabras
        if (seconds < 1.0 && words.Count >= 6)
        {
            double rate = words.Count / Math.Max(0.1, seconds);
            return new AsrAnomalyResult(
                true,
                AsrAnomalyType.SpeechRateExceeded,
                $"Tasa de habla anormalmente alta: {words.Count} palabras en {seconds:F3}s ({rate:F1} palabras/segundo).",
                rate);
        }

        // Caso C: Segmento de duración general con tasa > 8.0 palabras/segundo
        if (seconds >= 1.0)
        {
            double rate = words.Count / seconds;
            if (rate > 8.0 && words.Count >= 10)
            {
                return new AsrAnomalyResult(
                    true,
                    AsrAnomalyType.SpeechRateExceeded,
                    $"Tasa de habla excesiva para la duración del audio: {words.Count} palabras en {seconds:F1}s ({rate:F1} palabras/segundo).",
                    rate);
            }
        }

        return new AsrAnomalyResult(false, AsrAnomalyType.None, null);
    }

    /// <summary>
    /// Detecta bucles de repetición de n-gramas patológicos (1-gram hasta 5-gram).
    /// Permite repeticiones naturales cortas (ej. "sí, sí", "no, no, no", "vale, vale" hasta 3 veces).
    /// Detecta bucles degenerados (ej. "Bass Guitar, Electric Guitar, Guitar" repetido 4+ veces).
    /// </summary>
    public static AsrAnomalyResult CheckNgramRepetition(IReadOnlyList<string> words)
    {
        if (words.Count < 4)
        {
            return new AsrAnomalyResult(false, AsrAnomalyType.None, null);
        }

        // Comprobación 1: Bucle de 1-gram (la misma palabra repetida consecutivamente >= 4 veces)
        int currentWordRepeat = 1;
        for (int i = 1; i < words.Count; i++)
        {
            if (string.Equals(words[i], words[i - 1], StringComparison.OrdinalIgnoreCase))
            {
                currentWordRepeat++;
                if (currentWordRepeat >= 4)
                {
                    return new AsrAnomalyResult(
                        true,
                        AsrAnomalyType.NgramRepetitionLoop,
                        $"Palabra '{words[i]}' repetida consecutivamente {currentWordRepeat} veces (bucle 1-gram).",
                        currentWordRepeat);
                }
            }
            else
            {
                currentWordRepeat = 1;
            }
        }

        // Comprobación 2: Bucles de n-gramas consecutivos (n = 2 a 5)
        for (int n = 2; n <= 5; n++)
        {
            if (words.Count < n * 3) continue;

            for (int i = 0; i <= words.Count - (n * 3); i++)
            {
                int repetitions = CountConsecutiveNgramRepetitions(words, i, n);
                // Si una frase de 2+ palabras se repite consecutivamente 3 o más veces:
                if (repetitions >= 3)
                {
                    var pattern = string.Join(" ", words.Skip(i).Take(n));
                    return new AsrAnomalyResult(
                        true,
                        AsrAnomalyType.NgramRepetitionLoop,
                        $"Frase '{pattern}' repetida consecutivamente {repetitions} veces (bucle {n}-gram).",
                        repetitions);
                }
            }
        }

        // Comprobación 3: Ratio de diversidad léxica en segmentos medianos/largos
        // Si un segmento tiene >= 15 palabras y menos del 25% de palabras únicas, es una señal fuerte de bucle
        if (words.Count >= 15)
        {
            var uniqueCount = words.Distinct(StringComparer.OrdinalIgnoreCase).Count();
            double diversityRatio = (double)uniqueCount / words.Count;
            if (diversityRatio < 0.25)
            {
                return new AsrAnomalyResult(
                    true,
                    AsrAnomalyType.NgramRepetitionLoop,
                    $"Diversidad léxica anormalmente baja ({diversityRatio:P1}, {uniqueCount} únicas de {words.Count} palabras totales).",
                    diversityRatio);
            }
        }

        return new AsrAnomalyResult(false, AsrAnomalyType.None, null);
    }

    /// <summary>
    /// Detecta cuando un segmento está compuesto principalmente por términos del vocabulario personalizado
    /// sin conectores gramaticales reales (síntoma de que el modelo decodificó el prompt/hotwords en silencio).
    /// </summary>
    public static AsrAnomalyResult CheckVocabularyEchoing(
        IReadOnlyList<string> words,
        IReadOnlyList<string> vocabularyTerms)
    {
        if (words.Count < 4 || vocabularyTerms.Count == 0)
        {
            return new AsrAnomalyResult(false, AsrAnomalyType.None, null);
        }

        // Normalizar términos del vocabulario (palabras individuales)
        var vocabWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in vocabularyTerms)
        {
            if (string.IsNullOrWhiteSpace(term)) continue;
            var parts = term.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                if (p.Length >= 2) vocabWords.Add(p);
            }
        }

        if (vocabWords.Count == 0)
        {
            return new AsrAnomalyResult(false, AsrAnomalyType.None, null);
        }

        int matchingVocabCount = 0;
        int functionalWordCount = 0;

        foreach (var w in words)
        {
            if (vocabWords.Contains(w))
            {
                matchingVocabCount++;
            }
            if (FunctionalWords.Contains(w))
            {
                functionalWordCount++;
            }
        }

        double vocabRatio = (double)matchingVocabCount / words.Count;
        double functionalRatio = (double)functionalWordCount / words.Count;

        // Si >= 70% de las palabras del segmento coinciden con el vocabulario, y hay menos de un 15% de palabras funcionales
        if (vocabRatio >= 0.70 && functionalRatio < 0.15 && words.Count >= 5)
        {
            return new AsrAnomalyResult(
                true,
                AsrAnomalyType.VocabularyEchoing,
                $"Segmento compuesto predominantemente por vocabulario de contexto ({vocabRatio:P0}) sin conectores gramaticales ({functionalRatio:P0}).",
                vocabRatio);
        }

        // Comprobación de coincidencia de secuencia: si al menos 5 palabras aparecen en el mismo orden que el vocabulario
        if (words.Count >= 5 && vocabularyTerms.Count >= 5)
        {
            var cleanVocabList = vocabularyTerms
                .SelectMany(t => t.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries))
                .Where(w => w.Length >= 2)
                .ToList();

            int maxConsecutiveOrderMatch = 0;
            int vocabIdx = 0;
            int currentMatch = 0;

            for (int i = 0; i < words.Count && vocabIdx < cleanVocabList.Count; i++)
            {
                while (vocabIdx < cleanVocabList.Count && !string.Equals(words[i], cleanVocabList[vocabIdx], StringComparison.OrdinalIgnoreCase))
                {
                    vocabIdx++;
                }

                if (vocabIdx < cleanVocabList.Count && string.Equals(words[i], cleanVocabList[vocabIdx], StringComparison.OrdinalIgnoreCase))
                {
                    currentMatch++;
                    vocabIdx++;
                    maxConsecutiveOrderMatch = Math.Max(maxConsecutiveOrderMatch, currentMatch);
                }
                else
                {
                    currentMatch = 0;
                }
            }

            if (maxConsecutiveOrderMatch >= 5 && (double)maxConsecutiveOrderMatch / words.Count >= 0.60)
            {
                return new AsrAnomalyResult(
                    true,
                    AsrAnomalyType.VocabularyEchoing,
                    $"Secuencia literal de {maxConsecutiveOrderMatch} términos coincidente con el orden del glosario de entrada.",
                    maxConsecutiveOrderMatch);
            }
        }

        return new AsrAnomalyResult(false, AsrAnomalyType.None, null);
    }

    private static int CountConsecutiveNgramRepetitions(IReadOnlyList<string> words, int startIndex, int n)
    {
        int repetitions = 1;
        int nextStart = startIndex + n;

        while (nextStart + n <= words.Count)
        {
            bool match = true;
            for (int k = 0; k < n; k++)
            {
                if (!string.Equals(words[startIndex + k], words[nextStart + k], StringComparison.OrdinalIgnoreCase))
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                repetitions++;
                nextStart += n;
            }
            else
            {
                break;
            }
        }

        return repetitions;
    }

    private static List<string> ExtractWords(string text)
    {
        var rawWords = text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>(rawWords.Length);
        foreach (var w in rawWords)
        {
            var trimmed = w.Trim();
            if (trimmed.Length > 0 && char.IsLetterOrDigit(trimmed[0]))
            {
                result.Add(trimmed);
            }
        }
        return result;
    }
}

