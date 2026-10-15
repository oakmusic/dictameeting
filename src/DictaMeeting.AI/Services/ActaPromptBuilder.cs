using System.Text;
using System.Text.RegularExpressions;
using DictaMeeting.AI.Models;
using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Meetings.Vocabulary;

namespace DictaMeeting.AI.Services;

/// <summary>
/// Constructor y saneador de prompts y respuestas para generación de actas.
/// Centraliza la lógica de prompts para evitar duplicación entre diferentes proveedores de IA.
/// </summary>
public static class ActaPromptBuilder
{
    public static string BuildPrompt(Meeting meeting, ActaGenerationOptions options, IVocabularyService? vocabularyService = null)
    {
        var sb = new StringBuilder();
        var langStr = options.Language == LanguageMode.English ? "English" : "Español";

        sb.AppendLine("Eres un asistente ejecutivo de alta precisión especializado en redactar actas de reuniones oficiales.");
        sb.AppendLine($"Debes redactar el acta obligatoriamente en el idioma: {langStr}.");
        sb.AppendLine();
        sb.AppendLine("REGLAS ESTRICTAS DE FIDELIDAD:");
        sb.AppendLine("1. Basa todo el contenido EXCLUSIVAMENTE en la transcripción y metadatos proporcionados.");
        sb.AppendLine("2. NO inventes información, ni fechas, ni nombres, ni cargos, ni conclusiones.");
        sb.AppendLine("3. NO inventes decisiones no tomadas.");
        sb.AppendLine("4. Diferencia claramente entre una propuesta o idea debatida y un acuerdo o decisión firme.");
        sb.AppendLine("5. Si un dato no se menciona con certeza o está incompleto, indícalo expresamente como pendiente o dudoso.");
        sb.AppendLine();

        sb.AppendLine($"Nivel de detalle requerido: {options.DetailLevel}.");
        sb.AppendLine(GetDetailLevelGuidance(options.DetailLevel, options.Language));
        sb.AppendLine();

        if (options.IsImpersonal)
        {
            if (options.Language == LanguageMode.English)
            {
                sb.AppendLine("MANDATORY IMPERSONAL MINUTES DIRECTIVES:");
                sb.AppendLine("This option is ENABLED because participant identities are not reliable.");
                sb.AppendLine("1. Do NOT attribute any intervention, opinion, proposal or statement to specific participants.");
                sb.AppendLine("2. NEVER allow SPEAKER_00, SPEAKER_01, or any technical/numeric speaker identifier to appear in any section of the minutes.");
                sb.AppendLine("3. Do not invent names or guess who said what.");
                sb.AppendLine("4. Write the content in a STRICTLY IMPERSONAL manner focused on the meeting content, using phrasing such as:");
                sb.AppendLine("   - \"It was raised that...\"");
                sb.AppendLine("   - \"It was proposed that...\"");
                sb.AppendLine("   - \"It was agreed that...\"");
                sb.AppendLine("   - \"It was analyzed that...\"");
                sb.AppendLine("   - \"It was identified that...\"");
                sb.AppendLine("   - \"Pending decision regarding...\"");
                sb.AppendLine("5. Retain all decisions, proposals, action items, and topics discussed even if the speaker is unidentified.");
                sb.AppendLine("6. If an action item responsibility is explicitly tied to a person whose name is known and reliable, keep it; if not (or corresponds to a code like SPEAKER_XX), express it impersonally (e.g. \"Team\", \"TBD\", etc.).");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("DIRECTRICES MANDATORIAS DE ACTA IMPERSONAL:");
                sb.AppendLine("Esta opción se encuentra ACTIVADA porque los nombres o identidades de los participantes no son fiables.");
                sb.AppendLine("1. NO atribuyas ninguna intervención, opinión, propuesta o declaración a participantes concretos.");
                sb.AppendLine("2. NUNCA debe aparecer SPEAKER_00, SPEAKER_01, ni ningún identificador técnico o numérico de hablante en ninguna sección del acta.");
                sb.AppendLine("3. Tampoco inventes nombres ni intentes deducir quién dijo cada cosa.");
                sb.AppendLine("4. Redacta el contenido de forma ESTRICTAMENTE IMPERSONAL y centrada en el contenido de la reunión, empleando fórmulas como:");
                sb.AppendLine("   - \"Se ha planteado...\"");
                sb.AppendLine("   - \"Se ha propuesto...\"");
                sb.AppendLine("   - \"Se ha acordado...\"");
                sb.AppendLine("   - \"Se ha analizado...\"");
                sb.AppendLine("   - \"Se ha identificado...\"");
                sb.AppendLine("   - \"Queda pendiente...\"");
                sb.AppendLine("5. Las decisiones, propuestas, acciones y temas tratados deben conservarse e incluirse aunque no se pueda identificar quién los dijo.");
                sb.AppendLine("6. Si una responsabilidad en las acciones está explícitamente asociada a una persona y su nombre es fiable, mantenla; si no lo es (o corresponde a un código como SPEAKER_XX), exprésala de forma impersonal (ej. \"Equipo\", \"Por designar\", etc.).");
                sb.AppendLine();
            }
        }
        else
        {
            if (options.Language == LanguageMode.English)
            {
                sb.AppendLine("PARTICIPANT ATTRIBUTION AND SPEAKER HANDLING GUIDELINES:");
                sb.AppendLine("1. HEADER ('## Participants' section): In the participants list, include ONLY the user-provided participant names specified in the structure below. NEVER include technical identifiers like 'SPEAKER_XX' or 'speaker_00'.");
                sb.AppendLine("2. BODY OF MINUTES - ATTRIBUTION TO PARTICIPANTS WITH REAL NAMES: For participants with an assigned real name (e.g. user-introduced names or names in the transcript like 'Laura', 'Charles'), attribute their contributions, opinions, and commitments to them by name (e.g. 'Laura mentioned that...', 'Charles proposed...').");
                sb.AppendLine("3. BODY OF MINUTES - 'SPEAKER_XX' INTERVENTIONS (UNASSIGNED VOICES):");
                sb.AppendLine("   - If speakers with technical identifiers like 'SPEAKER_00', 'SPEAKER_01', 'speaker_XX' appear in the transcript (because no real name was assigned), NEVER treat 'SPEAKER_XX' as a person's name nor write things like 'SPEAKER_00 commented that...', 'according to SPEAKER_01...', 'SPEAKER_00 proposed...'.");
                sb.AppendLine("   - Any contribution from a 'SPEAKER_XX' MUST be documented in an IMPERSONAL way (e.g. 'it was commented that...', 'it was proposed that...', 'it was pointed out that...', 'discussion was held regarding...', 'it was agreed that...').");
                sb.AppendLine("   - Technical identifiers like 'SPEAKER_XX', 'SPEAKER_00', etc., must NEVER appear anywhere in the written text of the final minutes.");
                sb.AppendLine("4. ACTION ITEMS TABLE: If the owner of an action item was a 'SPEAKER_XX', do not put that technical identifier as the responsible party; use an impersonal term such as 'Team', 'TBD', or the relevant department.");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("DIRECTRICES DE ATRIBUCIÓN DE PARTICIPANTES Y TRATAMIENTO DE LOCUTORES:");
                sb.AppendLine("1. CABECERA (SECCIÓN '## Participantes'): En la lista de participantes, incluye ÚNICAMENTE los nombres reales de participantes introducidos por el usuario especificados en la estructura inferior. NUNCA incluyas identificadores técnicos como 'SPEAKER_XX' ni 'speaker_00'.");
                sb.AppendLine("2. CUERPO DEL ACTA - PARTICIPANTES CON NOMBRE REAL: Para aquellos participantes con un nombre propio asignado (ej. nombres introducidos por el usuario o asignados en la transcripción como 'Laura', 'Carlos', etc.), atribúyeles sus opiniones, propuestas y compromisos por su nombre (ej. 'Laura comentó que...', 'Carlos propuso...').");
                sb.AppendLine("3. CUERPO DEL ACTA - INTERVENCIONES DE 'SPEAKER_XX' (VOCES NO ASIGNADAS):");
                sb.AppendLine("   - Si en la transcripción intervienen hablantes identificados técnicamente como 'SPEAKER_00', 'SPEAKER_01', 'speaker_XX', etc. (debido a que no se les asignó un nombre por olvido o falta de certeza), NUNCA los trates como personas reales ni escribas cosas como 'SPEAKER_00 ha comentado...', 'según SPEAKER_01...', 'SPEAKER_00 propuso...'.");
                sb.AppendLine("   - Toda aportación correspondiente a un 'SPEAKER_XX' debe documentarse obligatoriamente de FORMA IMPERSONAL (por ejemplo: 'se ha comentado que...', 'se ha propuesto...', 'se señaló que...', 'se debatió sobre...', 'se acordó que...').");
                sb.AppendLine("   - NUNCA debe aparecer la palabra 'SPEAKER_XX', 'SPEAKER_00', 'SPEAKER_01', etc., en ninguna parte del texto redactado del acta.");
                sb.AppendLine("4. TABLA DE ACCIONES: Si el responsable de una tarea era un 'SPEAKER_XX', no pongas dicho identificador como responsable; usa un término impersonal como 'Equipo', 'Por determinar' o el área correspondiente.");
                sb.AppendLine();
            }
        }

        sb.AppendLine("ESTRUCTURA DEL ACTA:");
        sb.AppendLine("# Acta de la Reunión");
        sb.AppendLine("## Información General");
        sb.AppendLine($"- Título: {meeting.Title}");
        sb.AppendLine($"- Fecha: {meeting.Date:dd/MM/yyyy}");
        if (meeting.StartTime.HasValue) sb.AppendLine($"- Hora de inicio: {meeting.StartTime.Value:HH:mm}");
        sb.AppendLine($"- Duración: {(int)meeting.Duration.TotalHours:D2}:{meeting.Duration.Minutes:D2}:{meeting.Duration.Seconds:D2}");
        if (!string.IsNullOrWhiteSpace(meeting.Organizer)) sb.AppendLine($"- Organizador: {meeting.Organizer}");
        if (!string.IsNullOrWhiteSpace(meeting.Company)) sb.AppendLine($"- Empresa: {meeting.Company}");
        sb.AppendLine();

        if (options.IncludeParticipants)
        {
            sb.AppendLine("## Participantes");
            var userParticipants = meeting.GetUserParticipants();

            if (userParticipants.Count > 0)
            {
                foreach (var name in userParticipants)
                {
                    sb.AppendLine($"- {name}");
                }
            }
            else
            {
                if (options.IsImpersonal)
                {
                    sb.AppendLine("- (Asistentes a la reunión - identidades individuales no especificadas por modalidad impersonal)");
                }
                else
                {
                    sb.AppendLine("- (Sin participantes registrados)");
                }
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Resumen Ejecutivo");
        sb.AppendLine("## Temas Tratados");

        if (options.IncludeDecisions)
        {
            sb.AppendLine("## Decisiones Acordadas");
        }

        if (options.IncludeActionItems)
        {
            sb.AppendLine("## Acciones Acordadas");
            sb.AppendLine("| Acción | Responsable | Fecha Límite |");
            sb.AppendLine("|---|---|---|");
        }

        if (options.IncludePendingQuestions)
        {
            sb.AppendLine("## Cuestiones Pendientes");
        }

        sb.AppendLine("## Próximos Pasos");
        sb.AppendLine();

        var vocabTerms = vocabularyService?.GetOfficialWords() ?? options.VocabularyTerms;
        if (vocabTerms != null && vocabTerms.Count > 0)
        {
            sb.AppendLine("=== GLOSARIO Y TÉRMINOS OFICIALES DE LA REUNIÓN ===");
            sb.AppendLine("Conserva fielmente su grafía y capitalización oficial si aparecen citados en el acta:");
            sb.AppendLine(string.Join(", ", vocabTerms));
            sb.AppendLine("=== FIN DEL GLOSARIO ===");
            sb.AppendLine();
        }

        sb.AppendLine("=== TRANSCRIPCIÓN COMPLETA DE LA REUNIÓN ===");
        foreach (var segment in meeting.Transcript)
        {
            var speaker = string.IsNullOrWhiteSpace(segment.SpeakerDisplayName)
                ? (string.IsNullOrWhiteSpace(segment.SpeakerId) ? "Hablante" : segment.SpeakerId)
                : segment.SpeakerDisplayName;

            sb.AppendLine($"[{segment.FormattedStartTime}] {speaker}: {segment.Text}");
        }
        sb.AppendLine("=== FIN DE LA TRANSCRIPCIÓN ===");

        return sb.ToString();
    }

    public static string BuildSystemPrompt(ActaGenerationOptions options)
    {
        var systemPrompt = $"Eres un asistente ejecutivo de alta precisión especializado en redactar actas de reuniones oficiales.\n" +
                           $"Idioma obligatorio: {(options.Language == LanguageMode.English ? "English" : "Español")}.\n" +
                           $"Nivel de detalle: {options.DetailLevel}.\n\n" +
                           "REGLAS CRÍTICAS:\n" +
                           "- Fidelidad absoluta al texto de la transcripción.\n" +
                           "- No asumas ni inventes conclusiones ni fechas no debatidas.\n" +
                           "- Diferencia opiniones de compromisos formales.";

        if (options.IsImpersonal)
        {
            systemPrompt += "\n\nMODALIDAD ACTA IMPERSONAL:\n" +
                            "- Redacta el acta de forma estrictamente impersonal y centrada en la reunión.\n" +
                            "- NO atribuyas ninguna intervención a participantes concretos.\n" +
                            "- NUNCA debe aparecer SPEAKER_00, SPEAKER_01 ni ningún identificador de hablante en el acta redactada.\n" +
                            "- Tampoco inventes nombres ni intentes deducir quién dijo cada cosa.\n" +
                            "- Emplea expresiones como 'Se ha planteado...', 'Se ha propuesto...', 'Se ha acordado...', 'Se ha analizado...', 'Se ha identificado...', 'Queda pendiente...'.\n" +
                            "- Conserva todas las decisiones, propuestas, temas y acciones aunque no se identifique quién las dijo.\n" +
                            "- Si una responsabilidad está explícitamente asociada a una persona y su nombre es fiable, mantenla; si no lo es, exprésala de forma impersonal.";
        }
        else
        {
            systemPrompt += "\n\nMODALIDAD CON ATRIBUCIÓN A PARTICIPANTES:\n" +
                            "- En la sección de participantes, incluye ÚNICAMENTE los nombres reales de los participantes introducidos por el usuario.\n" +
                            "- Atribuye intervenciones a participantes con nombre propio conocido.\n" +
                            "- Si en la transcripción hay locutores con identificador técnico (SPEAKER_00, SPEAKER_01, speaker_XX...), NUNCA los trates como nombres de personas ni escribas 'speaker_00 ha comentado...'. Documenta sus aportaciones de forma IMPERSONAL ('se ha comentado que...', 'se ha propuesto...').\n" +
                            "- NUNCA debe figurar ningún identificador 'SPEAKER_XX' en el acta redactada.";
        }

        return systemPrompt;
    }

    public static string GetDetailLevelGuidance(ActaDetailLevel level, LanguageMode language)
    {
        if (language == LanguageMode.English)
        {
            return level switch
            {
                ActaDetailLevel.Breve => "Brief level: Synthesize the essential core directly without extending into debate details or minor back-and-forth.",
                ActaDetailLevel.Normal => "Normal level: Balanced executive minutes, developing each topic clearly and highlighting key agreements and tasks.",
                ActaDetailLevel.Detallada => "Detailed level: Thoroughly develop each topic discussed, documenting positions, arguments, context of each decision, and precise commitments and owners.",
                ActaDetailLevel.Exhaustiva => "Comprehensive level: Extensively and meticulously document all topics, discussions, participant arguments, evaluated alternatives, detailed context of each decision, complete action items table, and all open questions.",
                _ => string.Empty
            };
        }

        return level switch
        {
            ActaDetailLevel.Breve => "Nivel breve: Sintetiza lo esencial de forma concisa y directa, sin extenderse en los pormenores del debate.",
            ActaDetailLevel.Normal => "Nivel normal: Acta ejecutiva equilibrada, desarrollando cada tema con claridad y destacando los acuerdos y tareas clave.",
            ActaDetailLevel.Detallada => "Nivel detallado: Desarrolla ampliamente cada tema tratado, documentando las posturas y argumentos debatidos, el contexto de cada decisión, y especificando con precisión los compromisos y responsables.",
            ActaDetailLevel.Exhaustiva => "Nivel exhaustivo: Documenta de forma amplia y minuciosa todos los temas, debates, argumentos de los participantes, alternativas evaluadas, contexto detallado de cada acuerdo, tabla de acciones completa y todas las cuestiones abiertas o pendientes.",
            _ => string.Empty
        };
    }

    /// <summary>
    /// Construye el prompt de sistema para el análisis de un fragmento individual en la estrategia jerárquica.
    /// </summary>
    public static string BuildChunkAnalysisSystemPrompt(ActaGenerationOptions options)
    {
        var langStr = options.Language == LanguageMode.English ? "English" : "Español";
        var sb = new StringBuilder();
        sb.AppendLine("Eres un analista ejecutivo de máxima precisión y fidelidad factual.");
        sb.AppendLine($"Idioma de trabajo: {langStr}.");
        sb.AppendLine("Tu misión es extraer y estructurar rigurosamente los hechos acontecidos en este fragmento temporal de la reunión.");
        sb.AppendLine("REGLAS ESTRICTAS:");
        sb.AppendLine("1. Basa la extracción EXCLUSIVAMENTE en el texto del fragmento.");
        sb.AppendLine("2. NO inventes información, datos, nombres ni conclusiones.");
        sb.AppendLine("3. Diferencia estrictamente una propuesta debatida de una decisión o acuerdo firme.");
        sb.AppendLine("4. Conserva marcas temporales relevantes y nombres de participantes reales.");

        if (options.IsImpersonal)
        {
            sb.AppendLine("5. Modalidad impersonal ACTIVADA: No atribuyas declaraciones a participantes con identificadores técnicos (SPEAKER_XX). Redacta en forma impersonal.");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Construye el prompt de usuario para extraer hechos y acuerdos de un fragmento temporal específico.
    /// </summary>
    public static string BuildChunkAnalysisPrompt(
        TranscriptChunk chunk,
        Meeting meeting,
        ActaGenerationOptions options,
        IVocabularyService? vocabularyService = null)
    {
        var sb = new StringBuilder();
        var langStr = options.Language == LanguageMode.English ? "English" : "Español";

        sb.AppendLine($"Analiza el siguiente fragmento de la reunión '{meeting.Title}' ({chunk.TimeRange}, Fragmento #{chunk.Index + 1}).");
        sb.AppendLine($"Idioma de respuesta requerido: {langStr}.");
        sb.AppendLine();
        sb.AppendLine("Extrae de forma concisa y estructurada la siguiente información del fragmento:");
        sb.AppendLine("1. TEMAS Y DISCUSIONES: Puntos clave abordados en este tramo, argumentos principales y contexto temporal.");
        sb.AppendLine("2. PROPUESTAS Y DEBATE: Opciones planteadas, dudas o alternativas expuestas.");
        sb.AppendLine("3. ACUERDOS Y DECISIONES: Decisiones firmes adoptadas durante este tramo (si las hubo).");
        sb.AppendLine("4. ACCIONES Y RESPONSABLES: Tareas acordadas, quién es responsable (o 'Equipo' si es impersonal) y fechas límite si se mencionaron.");
        sb.AppendLine("5. CUESTIONES PENDIENTES: Puntos no resueltos o dejados para más adelante.");
        sb.AppendLine();

        var vocabTerms = vocabularyService?.GetOfficialWords() ?? options.VocabularyTerms;
        if (vocabTerms != null && vocabTerms.Count > 0)
        {
            sb.AppendLine($"Glosario oficial a respetar: {string.Join(", ", vocabTerms)}");
            sb.AppendLine();
        }

        sb.AppendLine($"=== TRANSCRIPCIÓN DEL FRAGMENTO ({chunk.TimeRange}) ===");
        foreach (var segment in chunk.Segments)
        {
            var speaker = string.IsNullOrWhiteSpace(segment.SpeakerDisplayName)
                ? (string.IsNullOrWhiteSpace(segment.SpeakerId) ? "Hablante" : segment.SpeakerId)
                : segment.SpeakerDisplayName;

            sb.AppendLine($"[{segment.FormattedStartTime}] {speaker}: {segment.Text}");
        }
        sb.AppendLine("=== FIN DEL FRAGMENTO ===");

        return sb.ToString();
    }

    /// <summary>
    /// Construye el prompt de sistema para la síntesis global de todos los fragmentos analizados.
    /// </summary>
    public static string BuildGlobalSynthesisSystemPrompt(ActaGenerationOptions options)
    {
        var langStr = options.Language == LanguageMode.English ? "English" : "Español";
        var sb = new StringBuilder();
        sb.AppendLine("Eres un integrador y sintetizador ejecutivo de máxima precisión.");
        sb.AppendLine($"Idioma obligatorio: {langStr}.");
        sb.AppendLine("Tu tarea es integrar y consolidar los análisis de las distintas etapas de una reunión larga en una síntesis global coherente.");
        sb.AppendLine("REGLAS ESTRICTAS:");
        sb.AppendLine("1. Integra temas que se trataron de forma discontinua en varios momentos de la reunión.");
        sb.AppendLine("2. Conserva íntegramente los acuerdos y conclusiones del final de la reunión tanto como las del principio.");
        sb.AppendLine("3. Elimina duplicidades causadas por la superposición de fragmentos.");
        sb.AppendLine("4. Diferencia con precisión acuerdos adoptados de propuestas pendientes.");
        sb.AppendLine("5. NO inventes información no contenida en los análisis parciales.");

        if (options.IsImpersonal)
        {
            sb.AppendLine("6. Redacción estrictamente impersonal sin identificadores SPEAKER_XX.");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Construye el prompt de usuario para integrar las síntesis de todos los fragmentos en una representación global.
    /// </summary>
    public static string BuildGlobalSynthesisPrompt(
        IReadOnlyList<string> chunkAnalyses,
        Meeting meeting,
        ActaGenerationOptions options)
    {
        var sb = new StringBuilder();
        var langStr = options.Language == LanguageMode.English ? "English" : "Español";

        sb.AppendLine($"Consolida los análisis parciales de la reunión '{meeting.Title}' ({meeting.Duration:hh\\:mm\\:ss}) en una síntesis global.");
        sb.AppendLine($"Idioma requerido: {langStr}.");
        sb.AppendLine($"Nivel de detalle del acta final: {options.DetailLevel}. {GetDetailLevelGuidance(options.DetailLevel, options.Language)}");
        sb.AppendLine();
        sb.AppendLine("INSTRUCCIONES DE CONSOLIDACIÓN:");
        sb.AppendLine("- Agrupa los temas por áreas temáticas coherentes, no por orden cronológico estricto.");
        sb.AppendLine("- Consolida la lista única y no duplicada de todas las decisiones acordadas.");
        sb.AppendLine("- Consolida la lista de acciones acordadas con responsables y fechas límite.");
        sb.AppendLine("- Agrupa todas las cuestiones que quedaron abiertas o pendientes de resolución.");
        sb.AppendLine("- Asegúrate de que las conclusiones tomadas al cierre de la reunión figuren con total claridad.");
        sb.AppendLine();

        sb.AppendLine("=== ANÁLISIS DE LOS FRAGMENTOS TEMPORALES ===");
        for (int i = 0; i < chunkAnalyses.Count; i++)
        {
            sb.AppendLine($"--- FRAGMENTO #{i + 1} ---");
            sb.AppendLine(chunkAnalyses[i]);
            sb.AppendLine();
        }
        sb.AppendLine("=== FIN DE ANÁLISIS DE FRAGMENTOS ===");

        return sb.ToString();
    }

    /// <summary>
    /// Construye el prompt final para redactar el acta Markdown definitiva a partir de la síntesis global integrada.
    /// </summary>
    public static string BuildFinalDraftFromSynthesisPrompt(
        string globalSynthesis,
        Meeting meeting,
        ActaGenerationOptions options,
        IVocabularyService? vocabularyService = null)
    {
        var sb = new StringBuilder();
        var langStr = options.Language == LanguageMode.English ? "English" : "Español";

        sb.AppendLine("Eres un redactor ejecutivo de actas oficiales.");
        sb.AppendLine($"Redacta el acta final completa obligatoriamente en el idioma: {langStr}.");
        sb.AppendLine($"Nivel de detalle requerido: {options.DetailLevel}.");
        sb.AppendLine(GetDetailLevelGuidance(options.DetailLevel, options.Language));
        sb.AppendLine();

        if (options.IsImpersonal)
        {
            if (options.Language == LanguageMode.English)
            {
                sb.AppendLine("MANDATORY IMPERSONAL MINUTES DIRECTIVES:");
                sb.AppendLine("Write strictly in an impersonal style. NEVER include SPEAKER_XX or technical speaker identifiers.");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("DIRECTRICES MANDATORIAS DE ACTA IMPERSONAL:");
                sb.AppendLine("Redacta de forma estrictamente impersonal. NUNCA incluyas SPEAKER_XX ni identificadores técnicos de hablante.");
                sb.AppendLine();
            }
        }

        sb.AppendLine("ESTRUCTURA DEL ACTA OBLIGATORIA:");
        sb.AppendLine("# Acta de la Reunión");
        sb.AppendLine("## Información General");
        sb.AppendLine($"- Título: {meeting.Title}");
        sb.AppendLine($"- Fecha: {meeting.Date:dd/MM/yyyy}");
        if (meeting.StartTime.HasValue) sb.AppendLine($"- Hora de inicio: {meeting.StartTime.Value:HH:mm}");
        sb.AppendLine($"- Duración: {(int)meeting.Duration.TotalHours:D2}:{meeting.Duration.Minutes:D2}:{meeting.Duration.Seconds:D2}");
        if (!string.IsNullOrWhiteSpace(meeting.Organizer)) sb.AppendLine($"- Organizador: {meeting.Organizer}");
        if (!string.IsNullOrWhiteSpace(meeting.Company)) sb.AppendLine($"- Empresa: {meeting.Company}");
        sb.AppendLine();

        if (options.IncludeParticipants)
        {
            sb.AppendLine("## Participantes");
            var userParticipants = meeting.GetUserParticipants();
            if (userParticipants.Count > 0)
            {
                foreach (var name in userParticipants) sb.AppendLine($"- {name}");
            }
            else
            {
                sb.AppendLine(options.IsImpersonal
                    ? "- (Asistentes a la reunión - identidades individuales no especificadas por modalidad impersonal)"
                    : "- (Sin participantes registrados)");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Resumen Ejecutivo");
        sb.AppendLine("## Temas Tratados");

        if (options.IncludeDecisions)
        {
            sb.AppendLine("## Decisiones Acordadas");
        }

        if (options.IncludeActionItems)
        {
            sb.AppendLine("## Acciones Acordadas");
            sb.AppendLine("| Acción | Responsable | Fecha Límite |");
            sb.AppendLine("|---|---|---|");
        }

        if (options.IncludePendingQuestions)
        {
            sb.AppendLine("## Cuestiones Pendientes");
        }

        sb.AppendLine("## Próximos Pasos");
        sb.AppendLine();

        var vocabTerms = vocabularyService?.GetOfficialWords() ?? options.VocabularyTerms;
        if (vocabTerms != null && vocabTerms.Count > 0)
        {
            sb.AppendLine("=== GLOSARIO OFICIAL ===");
            sb.AppendLine(string.Join(", ", vocabTerms));
            sb.AppendLine("=== FIN DEL GLOSARIO ===");
            sb.AppendLine();
        }

        sb.AppendLine("=== SÍNTESIS CONSOLIDADA DE LA REUNIÓN (CONTIENE TODAS LAS ETAPAS, INICIO A FIN) ===");
        sb.AppendLine(globalSynthesis);
        sb.AppendLine("=== FIN DE LA SÍNTESIS ===");

        return sb.ToString();
    }

    /// <summary>
    /// Construye el prompt para continuar un acta interrumpida por límite de tokens en generación directa.
    /// </summary>
    public static string BuildContinuationPrompt(string partialMarkdown, Meeting meeting, ActaGenerationOptions options)
    {
        var langStr = options.Language == LanguageMode.English ? "English" : "Español";
        var sb = new StringBuilder();

        sb.AppendLine($"El acta de la reunión '{meeting.Title}' quedó incompleta porque la respuesta anterior alcanzó el límite de tokens.");
        sb.AppendLine($"Continúa la redacción exactamente donde se quedó, completando todas las secciones pendientes en {langStr}.");
        sb.AppendLine("NO repitas el texto ya generado al principio; retoma de inmediato con la siguiente sección o párrafo pendiente hasta el final.");
        sb.AppendLine();
        sb.AppendLine("=== TEXTO PARCIAL YA GENERADO (ÚLTIMAS LÍNEAS) ===");
        var lines = partialMarkdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        int takeLines = Math.Min(15, lines.Length);
        for (int i = lines.Length - takeLines; i < lines.Length; i++)
        {
            sb.AppendLine(lines[i]);
        }
        sb.AppendLine("=== FIN DEL TEXTO PREVIO ===");

        return sb.ToString();
    }

    public static string SanitizeActaMarkdown(string markdown, Meeting meeting, ActaGenerationOptions options)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return markdown;

        var result = markdown;

        // 1. Limpieza de la sección de Participantes en la cabecera
        result = SanitizeParticipantsSection(result, meeting, options);

        // 2. Limpieza de frases en el cuerpo donde SPEAKER_XX se usa con verbos de habla o propuesta
        result = Regex.Replace(
            result,
            @"(?i)(?:(?<bol>^|[.\n]\s*|\*\s*|-\s*|\d+\.\s*)|(?<mid>\b))speaker[_\s\-]?\d+\s+(?<verb>ha\s+comentado|comentó|ha\s+propuesto|propuso|ha\s+señalado|señaló|ha\s+indicado|indicó|ha\s+sugerido|sugirió|ha\s+manifestado|manifestó|ha\s+mencionado|mencionó|ha\s+explicado|explicó|ha\s+planteado|planteó|ha\s+destacado|destacó|ha\s+acordado|acordó|ha\s+afirmado|afirmó|ha\s+dicho|dijo|expuso|ha\s+expuesto|intervino|ha\s+intervenido|subrayó|ha\s+subrayado|añadió|ha\s+añadido|agregó|ha\s+agregado|consultó|ha\s+consultado|preguntó|ha\s+preguntado|aclaró|ha\s+aclarado|reiteró|ha\s+reiterado|consideró|ha\s+considerado|compartió|ha\s+compartido|presentó|ha\s+presentado|detalló|ha\s+detallado|informó|ha\s+informado|revisó|ha\s+revisado)\b",
            m =>
            {
                var bol = m.Groups["bol"];
                var verb = m.Groups["verb"].Value;
                if (bol.Success && !string.IsNullOrEmpty(bol.Value))
                {
                    return $"{bol.Value}Se {verb.ToLowerInvariant()}";
                }
                return $"se {verb.ToLowerInvariant()}";
            });

        // Versión en inglés si corresponde
        result = Regex.Replace(
            result,
            @"(?i)(?:(?<bol>^|[.\n]\s*|\*\s*|-\s*|\d+\.\s*)|(?<mid>\b))speaker[_\s\-]?\d+\s+(?<verb>commented|proposed|pointed\s+out|indicated|suggested|mentioned|explained|stated|noted|agreed|said|asked|clarified|shared|presented|added|inquired)\b",
            m =>
            {
                var bol = m.Groups["bol"];
                var verb = m.Groups["verb"].Value;
                if (bol.Success && !string.IsNullOrEmpty(bol.Value))
                {
                    return $"{bol.Value}It was {verb.ToLowerInvariant()} that";
                }
                return $"it was {verb.ToLowerInvariant()} that";
            });

        // 3. Limpieza de giros preposicionales
        result = Regex.Replace(
            result,
            @"(?i)\bsegún\s+speaker[_\s\-]?\d+,?",
            "según se comentó,");

        result = Regex.Replace(
            result,
            @"(?i)\baccording\s+to\s+speaker[_\s\-]?\d+,?",
            "as noted,");

        // 4. Limpieza en tablas de acciones: "| SPEAKER_00 |" -> "| Equipo |"
        result = Regex.Replace(
            result,
            @"(?i)\|\s*speaker[_\s\-]?\d+\s*\|",
            options.Language == LanguageMode.English ? "| Team |" : "| Equipo |");

        // 5. Cualquier mención residual de SPEAKER_XX en el texto
        result = Regex.Replace(
            result,
            @"(?i)\bspeaker[_\s\-]?\d+\b",
            options.Language == LanguageMode.English ? "a participant" : "un participante");

        return result;
    }

    public static string SanitizeParticipantsSection(string markdown, Meeting meeting, ActaGenerationOptions options)
    {
        var match = Regex.Match(
            markdown,
            @"(?im)^(##\s+(?:Participantes|Participants)\b.*)$");

        if (!match.Success) return markdown;

        var headerIndex = match.Index;
        var afterHeader = markdown.Substring(headerIndex + match.Length);

        var nextHeaderMatch = Regex.Match(
            afterHeader,
            @"(?im)^#{1,2}\s+");

        int sectionLength = nextHeaderMatch.Success ? nextHeaderMatch.Index : afterHeader.Length;
        var sectionContent = afterHeader.Substring(0, sectionLength);

        var lines = sectionContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var filteredLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("-") || trimmed.StartsWith("*"))
            {
                var participantName = trimmed.TrimStart('-', '*', ' ').Trim();
                if (Meeting.IsTechnicalSpeakerName(participantName))
                {
                    continue;
                }
            }
            filteredLines.Add(line);
        }

        var userParticipants = meeting.GetUserParticipants();
        bool hasBulletItems = filteredLines.Any(l => {
            var t = l.Trim();
            return (t.StartsWith("-") || t.StartsWith("*")) && !t.Contains("Sin participantes") && !t.Contains("no especificadas");
        });

        if (!hasBulletItems)
        {
            var replacementBuilder = new StringBuilder();
            replacementBuilder.AppendLine();
            if (userParticipants.Count > 0)
            {
                foreach (var up in userParticipants)
                {
                    replacementBuilder.AppendLine($"- {up}");
                }
            }
            else
            {
                if (options.IsImpersonal)
                {
                    replacementBuilder.AppendLine("- (Asistentes a la reunión - identidades individuales no especificadas por modalidad impersonal)");
                }
                else
                {
                    replacementBuilder.AppendLine("- (Sin participantes registrados)");
                }
            }
            replacementBuilder.AppendLine();

            return markdown.Substring(0, headerIndex + match.Length) +
                   replacementBuilder.ToString() +
                   markdown.Substring(headerIndex + match.Length + sectionLength);
        }

        var newSectionContent = string.Join(Environment.NewLine, filteredLines);
        return markdown.Substring(0, headerIndex + match.Length) +
               newSectionContent +
               markdown.Substring(headerIndex + match.Length + sectionLength);
    }
}
