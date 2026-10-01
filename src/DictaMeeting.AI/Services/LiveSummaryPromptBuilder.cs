using System.Text;
using System.Text.RegularExpressions;

namespace DictaMeeting.AI.Services;

/// <summary>
/// Generador y sanitizador de prompts ChatML para el modelo de resumen local Qwen2.5-1.5B-Instruct.
/// Aplica reglas anti-alucinación estrictas, preservación de terminología técnica y concisión (2–4 líneas).
/// </summary>
public static class LiveSummaryPromptBuilder
{
    public static string BuildPrompt(string previousSummary, string newTranscriptWindow, string language = "Spanish", IEnumerable<string>? vocabularyTerms = null)
    {
        bool isEnglish = language.Equals("English", StringComparison.OrdinalIgnoreCase) ||
                         language.Equals("en", StringComparison.OrdinalIgnoreCase);

        var systemPrompt = isEnglish ? BuildEnglishSystemPrompt(vocabularyTerms) : BuildSpanishSystemPrompt(vocabularyTerms);
        var userPrompt = isEnglish
            ? BuildEnglishUserPrompt(previousSummary, newTranscriptWindow)
            : BuildSpanishUserPrompt(previousSummary, newTranscriptWindow);

        var sb = new StringBuilder();
        sb.Append("<|im_start|>system\n");
        sb.Append(systemPrompt.Trim());
        sb.Append("<|im_end|>\n");
        sb.Append("<|im_start|>user\n");
        sb.Append(userPrompt.Trim());
        sb.Append("<|im_end|>\n");
        sb.Append("<|im_start|>assistant\n");

        return sb.ToString();
    }

    private static readonly Regex SentenceEndingRegex = new(
        @"(?<!\b(?:ej|etc|sr|sra|dr|dra|vs|prof|ing|pág|pag|num|núm|e\.g|i\.e))(?<!\b[A-Za-z])(?<!\d)[\.!\?](?=[""»\)]*(?:\s+|$))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string BuildSpanishSystemPrompt(IEnumerable<string>? vocabularyTerms = null)
    {
        var termsList = vocabularyTerms?.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        var termsRule = termsList != null && termsList.Count > 0
            ? $"\n10. Términos y nombres propios oficiales de la reunión que deben respetarse exactamente: {string.Join(", ", termsList)}."
            : string.Empty;

        return $"""
            Eres un asistente especializado en resumen en vivo de reuniones. Tu función es redactar un resumen sintético, fiel, claro y conciso en un único párrafo de aproximadamente 50 palabras de lo tratado en este tramo de la reunión.

            REGLAS CRÍTICAS:
            1. Redacción impersonal obligatoria: redacta siempre con fórmulas impersonales en tercera persona ("Se ha comentado que...", "Se ha explicado...", "Se ha expuesto...", "Se ha acordado..."). Prohibido usar la primera persona ("yo", "nosotros", "mi equipo", "trabajamos"). Por ejemplo, si en la transcripción se dice "en mi equipo trabajamos", debes redactar como "se ha comentado que en el equipo trabajan".
            2. Longitud y síntesis: redacta exactamente un único párrafo muy sintético de unas 50 palabras (aproximadamente 50 a 60 palabras, en torno a 2 o 3 frases breves). No intentes abarcar cada detalle menor; condensa únicamente las ideas clave y acuerdos.
            3. Frases completas y con sentido: cada oración debe estar completamente terminada y tener sentido íntegro por sí misma. Es obligatorio terminar la última frase de manera natural y con punto final. Jamás dejes una frase a medias, cortada o inconclusa.
            4. Prohibido repetir la transcripción completa o copiar frases literales línea por línea.
            5. Prohibido empezar con "Ninguno", "Resumen:", dos puntos ":" ni viñetas.
            6. Prohibido incluir muletillas vacías ("En esta reunión...", "En este tramo..."). Comienza directamente con la acción tratada en estilo impersonal ("Se ha explicado...", "Se ha debatido...", "Se ha acordado...").
            7. Basa la información EXCLUSIVAMENTE en lo dicho en el texto. No inventes datos, nombres ni fechas.
            8. Mantén intactos los nombres técnicos, proyectos y empresas (ej. "Docker", "Kubernetes", "WebSockets", "OAuth2").
            9. Prohibido mencionar a "DictaMeeting", a la propia aplicación o al asistente de IA en el resumen. El resumen trata exclusivamente sobre los temas abordados por los interlocutores en el audio.{termsRule}
            """;
    }

    private static string BuildEnglishSystemPrompt(IEnumerable<string>? vocabularyTerms = null)
    {
        var termsList = vocabularyTerms?.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        var termsRule = termsList != null && termsList.Count > 0
            ? $"\n10. Official meeting names and terms that must be preserved with exact spelling: {string.Join(", ", termsList)}."
            : string.Empty;

        return $"""
            You are a live meeting summary assistant. Your role is to write a synthetic, faithful, clear, and concise single-paragraph summary of approximately 50 words for this section of the meeting.

            CRITICAL RULES:
            1. Mandatory impersonal voice: always write using impersonal third-person phrasing ("It was discussed that...", "It was explained that...", "The team was reported to..."). Strictly never use first-person pronouns ("I", "we", "my team", "our").
            2. Length and synthesis: write exactly one very concise paragraph of about 50 words (around 50 to 60 words, roughly 2 or 3 short sentences). Do not try to include every minor detail; summarize only key points and decisions.
            3. Complete sentences: every sentence must be fully completed and make complete sense on its own. The final sentence must always end naturally with a period. Never leave a sentence half-finished or cut off.
            4. Never repeat the transcript verbatim or copy line-by-line dialogues.
            5. Never start with "None", "Summary:", colons ":", or bullet points.
            6. Never use introductory fluff ("In this meeting...", "In this section..."). Go straight to the points discussed using impersonal voice.
            7. Base information EXCLUSIVELY on what was stated in the text. Do not invent facts, names, or numbers.
            8. Preserve technical and product/project names intact.
            9. Strictly never mention "DictaMeeting", the application, or the AI assistant in the summary. The summary must refer solely to the speakers and topics in the transcript.{termsRule}
            """;
    }

    private static string BuildSpanishUserPrompt(string previousSummary, string newTranscriptWindow)
    {
        return $"""
            TRANSCRIPCIÓN DEL TRAMO DE LA REUNIÓN:
            {newTranscriptWindow.Trim()}

            INSTRUCCIÓN:
            Redacta en un único párrafo de aproximadamente 50 palabras un resumen conciso de las ideas clave tratadas en este fragmento. Redacta obligatoriamente de forma impersonal ("Se ha comentado...", "Se ha explicado..."), nunca en primera persona. Asegúrate de redactar oraciones completas y terminar con punto final:
            """;
    }

    private static string BuildEnglishUserPrompt(string previousSummary, string newTranscriptWindow)
    {
        return $"""
            SECTION TRANSCRIPT:
            {newTranscriptWindow.Trim()}

            INSTRUCTION:
            Write a single paragraph of approximately 50 words summarizing the key points discussed in this section using impersonal phrasing, never first-person. Ensure all sentences are fully completed and end with a period:
            """;
    }

    /// <summary>
    /// Limpia y sanitiza la salida del LLM eliminando introducciones prohibidas, comillas envolventes,
    /// artefactos de stop tokens y fragmentos de frases truncadas.
    /// </summary>
    public static string SanitizeSummaryOutput(string rawOutput, string previousSummary)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            return previousSummary;
        }

        var text = rawOutput.Trim();

        // 1. Eliminar tokens especiales si se filtraron
        text = text.Replace("<|im_end|>", "")
                   .Replace("<|endoftext|>", "")
                   .Replace("<|im_start|>", "");

        // 2. Eliminar bloques de código markdown
        if (text.StartsWith("```") && text.EndsWith("```"))
        {
            var lines = text.Split('\n');
            if (lines.Length >= 3)
            {
                text = string.Join("\n", lines.Skip(1).Take(lines.Length - 2)).Trim();
            }
        }

        // 3. Eliminar comillas envolventes
        if ((text.StartsWith("\"") && text.EndsWith("\"")) || (text.StartsWith("«") && text.EndsWith("»")))
        {
            text = text.Substring(1, text.Length - 2).Trim();
        }

        // 4. Eliminar prefijo accidental "Ninguno" o "None" al inicio
        text = Regex.Replace(text, @"^(?:Ninguno|None)\s*[:\.\-]*\s*", "", RegexOptions.IgnoreCase).Trim();

        // 5. Eliminar prefijos no deseados habituales
        var prefixesToRemove = new[]
        {
            @"^(?:En este tramo,?\s*(?:de\s+la\s+reunión,?\s*)?)",
            @"^(?:En este fragmento,?\s*(?:de\s+la\s+reunión,?\s*)?)",
            @"^(?:En esta reunión,?\s*(?:los\s+participantes\s+(?:comentan|hablan|debaten)(?:\s+(?:que|de))?|se\s+ha\s+hablado\s+de|se\s+está\s+tratando|se\s+comenta\s+que)?\s*)",
            @"^(?:Resumen(?:\s+(?:del\s+tramo|actualizado))?:\s*)",
            @"^(?:Summary(?:\s+update)?:\s*)",
            @"^(?:In this (?:meeting|section),?\s*(?:the\s+participants\s+discussed|we\s+discussed)?\s*)"
        };

        foreach (var pattern in prefixesToRemove)
        {
            text = Regex.Replace(text, pattern, "", RegexOptions.IgnoreCase).Trim();
        }

        // 6. Eliminar dos puntos o guiones iniciales si quedaron
        text = Regex.Replace(text, @"^[:\-•\s]+", "").Trim();

        // 7. Si contiene líneas que empiezan con dos puntos (como ': frase'), limpiarlas
        var cleanLines = text.Split('\n')
                             .Select(l => Regex.Replace(l.Trim(), @"^[:\-•\s]+", "").Trim())
                             .Where(l => !string.IsNullOrWhiteSpace(l))
                             .ToList();

        if (cleanLines.Count > 0)
        {
            text = string.Join(" ", cleanLines);
        }

        // 8. Eliminar menciones alucinadas a DictaMeeting si se colaron como persona, asistente o cláusula añadida
        text = Regex.Replace(text, @"(?:,\s*)?incluyendo\s+[^.!?]*?\bDictaMeeting\b", "", RegexOptions.IgnoreCase).Trim();
        text = Regex.Replace(text, @"\b(?:asistentes|usuarios|participantes)\s+(?:a|de)\s+DictaMeeting\b", "", RegexOptions.IgnoreCase).Trim();
        text = Regex.Replace(text, @"\b(?:en|de|para|con|a)\s+DictaMeeting\b", "", RegexOptions.IgnoreCase).Trim();
        text = Regex.Replace(text, @"\bDictaMeeting\b", "", RegexOptions.IgnoreCase).Trim();

        // 9. Asegurar que las oraciones estén completas y con sentido
        text = EnsureCompleteSentences(text);

        // Si quedó vacío o demasiado corto tras limpiar, conservar el resumen anterior
        if (string.IsNullOrWhiteSpace(text) || text.Length < 10)
        {
            return previousSummary;
        }

        return text;
    }

    /// <summary>
    /// Asegura que el resumen termine con una frase completa y con sentido,
    /// eliminando fragmentos truncados al final o cerrando adecuadamente la oración.
    /// </summary>
    public static string EnsureCompleteSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        text = text.Trim();

        // 1. Si ya termina con signo de puntuación de cierre (. ! ? o acompañados de comillas/paréntesis)
        if (Regex.IsMatch(text, @"[\.!\?][""»\)]*$"))
        {
            // Limpiar si accidentalmente quedó un conector colgando antes del punto final (ej. "...costo, pero.")
            text = Regex.Replace(text, @"[,;:\s]+(?:pero|y|o|que|de|del|la|el|se|a|en|con|and|or|but)\s*([\.!\?][""»\)]*)$", "$1", RegexOptions.IgnoreCase);
            return text.Trim();
        }

        // 2. Si no termina con signo de puntuación de cierre, buscar el último terminador de oración válido
        // Evitamos números con decimales (5.5) y abreviaturas comunes (ej., etc., sr.)
        var matches = SentenceEndingRegex.Matches(text);
        if (matches.Count > 0)
        {
            var lastMatch = matches[^1];
            int cutIndex = lastMatch.Index + lastMatch.Length;

            // Incluir comillas o paréntesis de cierre si los hay justo después
            while (cutIndex < text.Length && (text[cutIndex] == '"' || text[cutIndex] == '»' || text[cutIndex] == ')' || text[cutIndex] == '\''))
            {
                cutIndex++;
            }

            string truncatedAtSentence = text.Substring(0, cutIndex).Trim();

            // Si la parte completa tiene suficiente entidad (al menos 35 caracteres), la usamos descartando el fragmento roto
            if (truncatedAtSentence.Length >= 35)
            {
                return truncatedAtSentence;
            }
        }

        // 3. Si no hay terminador previo o el texto es una sola oración truncada,
        // limpiar conectores colgantes o palabras cortadas al final y añadir punto final
        string cleaned = text;

        // Limpiar hasta 3 conectores o palabras colgantes al final (ej. "debido a la" -> "")
        for (int i = 0; i < 3; i++)
        {
            string prev = cleaned;
            cleaned = Regex.Replace(cleaned,
                @"\s+(?:y|e|o|u|pero|mas|sino|que|de|del|a|al|en|con|por|para|hacia|sobre|tras|sin|se|la|el|los|las|un|una|unos|unas|lo|su|sus|este|esta|estos|estas|ese|esa|esos|esas|aquel|aquella|como|si|porque|debido\s+a(?:l)?|ya\s+que|puesto\s+que|and|or|but|the|a|an|of|to|in|with|by|for|on|at|that|which|as|if|because)\s*$",
                "", RegexOptions.IgnoreCase).Trim();

            if (cleaned == prev) break;
        }

        // Limpiar signos colgantes intermedios (, ; : -)
        cleaned = Regex.Replace(cleaned, @"[,;:\-\s]+$", "").Trim();

        if (cleaned.Length > 0 && !Regex.IsMatch(cleaned, @"[\.!\?][""»\)]*$"))
        {
            cleaned += ".";
        }

        return cleaned;
    }
}
