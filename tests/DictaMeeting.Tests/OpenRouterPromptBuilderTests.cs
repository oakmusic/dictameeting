using DictaMeeting.AI.Models;
using DictaMeeting.AI.Services;
using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Models;
using Xunit;

namespace DictaMeeting.Tests;

public class OpenRouterPromptBuilderTests
{
    private readonly OpenRouterActaService _actaService = new();

    [Fact]
    public void BuildPrompt_IncludesStrictAntiHallucinationRulesAndDecisions()
    {
        // Arrange
        var meeting = new Meeting
        {
            Title = "Comité de Dirección",
            Organizer = "Aritz Villodas",
            Company = "DictaCorp",
            Date = new DateTime(2026, 9, 21)
        };
        meeting.Participants.Add(new Speaker { Id = "SPEAKER_00", DisplayName = "Aritz Villodas" });
        meeting.Transcript.Add(new TranscriptSegment
        {
            SpeakerId = "SPEAKER_00",
            SpeakerDisplayName = "Aritz Villodas",
            Text = "Acordamos lanzar la versión beta en noviembre."
        });

        var options = new ActaGenerationOptions
        {
            Language = LanguageMode.Spanish,
            DetailLevel = ActaDetailLevel.Detallada,
            IncludeDecisions = true,
            IncludeActionItems = true
        };

        // Act
        var prompt = _actaService.BuildPrompt(meeting, options);

        // Assert
        Assert.Contains("REGLAS ESTRICTAS DE FIDELIDAD", prompt);
        Assert.Contains("NO inventes información", prompt);
        Assert.Contains("Diferencia claramente entre una propuesta o idea debatida y un acuerdo o decisión firme", prompt);
        Assert.Contains("## Decisiones Acordadas", prompt);
        Assert.Contains("| Acción | Responsable | Fecha Límite |", prompt);
        Assert.Contains("Comité de Dirección", prompt);
        Assert.Contains("Aritz Villodas", prompt);
        Assert.Contains("Acordamos lanzar la versión beta en noviembre.", prompt);
    }

    [Fact]
    public void BuildPrompt_WhenImpersonalIsTrue_IncludesMandatoryImpersonalDirectives()
    {
        // Arrange
        var meeting = new Meeting
        {
            Title = "Planificación de Sprint",
            Date = new DateTime(2026, 9, 22)
        };
        meeting.Participants.Add(new Speaker { Id = "SPEAKER_00", DisplayName = "" });
        meeting.Participants.Add(new Speaker { Id = "SPEAKER_01", DisplayName = "Laura Gómez" });
        meeting.Transcript.Add(new TranscriptSegment
        {
            SpeakerId = "SPEAKER_00",
            Text = "Propongo migrar la base de datos a PostgreSQL."
        });
        meeting.Transcript.Add(new TranscriptSegment
        {
            SpeakerId = "SPEAKER_01",
            SpeakerDisplayName = "Laura Gómez",
            Text = "De acuerdo, yo prepararé el esquema de migración."
        });

        var options = new ActaGenerationOptions
        {
            Language = LanguageMode.Spanish,
            DetailLevel = ActaDetailLevel.Normal,
            IncludeParticipants = true,
            IncludeDecisions = true,
            IncludeActionItems = true,
            IsImpersonal = true
        };

        // Act
        var prompt = _actaService.BuildPrompt(meeting, options);

        // Assert
        Assert.Contains("DIRECTRICES MANDATORIAS DE ACTA IMPERSONAL", prompt);
        Assert.Contains("NO atribuyas ninguna intervención, opinión, propuesta o declaración a participantes concretos", prompt);
        Assert.Contains("NUNCA debe aparecer SPEAKER_00, SPEAKER_01", prompt);
        Assert.Contains("Tampoco inventes nombres ni intentes deducir quién dijo cada cosa", prompt);
        Assert.Contains("Se ha planteado...", prompt);
        Assert.Contains("Se ha propuesto...", prompt);
        Assert.Contains("Se ha acordado...", prompt);
        Assert.Contains("Se ha analizado...", prompt);
        Assert.Contains("Se ha identificado...", prompt);
        Assert.Contains("Queda pendiente...", prompt);

        // Responsabilidades: fiable se mantiene, no fiable impersonal
        Assert.Contains("Si una responsabilidad en las acciones está explícitamente asociada a una persona y su nombre es fiable, mantenla", prompt);

        // En la lista de participantes estructurada, no debe aparecer SPEAKER_00 como participante
        var participantsSection = prompt.Substring(prompt.IndexOf("## Participantes"));
        var participantsEnd = participantsSection.IndexOf("## Resumen Ejecutivo");
        var participantsText = participantsSection.Substring(0, participantsEnd);
        Assert.DoesNotContain("SPEAKER_00", participantsText);
        Assert.Contains("Laura Gómez", participantsText);
    }

    [Fact]
    public void BuildPrompt_WhenImpersonalIsFalse_DoesNotIncludeImpersonalDirectives()
    {
        // Arrange
        var meeting = new Meeting
        {
            Title = "Reunión Ordinaria",
            Date = new DateTime(2026, 9, 22)
        };
        meeting.Participants.Add(new Speaker { Id = "SPEAKER_00", DisplayName = "Carlos" });

        var options = new ActaGenerationOptions
        {
            Language = LanguageMode.Spanish,
            IsImpersonal = false
        };

        // Act
        var prompt = _actaService.BuildPrompt(meeting, options);

        // Assert
        Assert.DoesNotContain("DIRECTRICES MANDATORIAS DE ACTA IMPERSONAL", prompt);
    }

    [Fact]
    public void BuildPrompt_NonImpersonal_IncludesUserParticipantsAndImpersonalRulesForUnassignedSpeakers()
    {
        // Arrange
        var meeting = new Meeting
        {
            Title = "Reunión de Estrategia",
            Date = new DateTime(2026, 9, 28)
        };
        // Participantes introducidos por el usuario
        meeting.ExpectedParticipants.Add("Aritz Villodas");
        meeting.ExpectedParticipants.Add("Marta Sánchez");

        // Participantes tras diarización con hablantes técnicos no asignados
        meeting.Participants.Add(new Speaker { Id = "SPEAKER_00", DisplayName = "SPEAKER_00" });
        meeting.Participants.Add(new Speaker { Id = "SPEAKER_01", DisplayName = "SPEAKER_01" });

        meeting.Transcript.Add(new TranscriptSegment
        {
            SpeakerId = "SPEAKER_00",
            Text = "Hemos revisado los números del trimestre."
        });

        var options = new ActaGenerationOptions
        {
            Language = LanguageMode.Spanish,
            DetailLevel = ActaDetailLevel.Normal,
            IncludeParticipants = true,
            IsImpersonal = false
        };

        // Act
        var prompt = _actaService.BuildPrompt(meeting, options);

        // Assert
        // 1. Cabecera ## Participantes en la estructura contiene sólo los nombres introducidos por el usuario
        var estructuraIndex = prompt.IndexOf("ESTRUCTURA DEL ACTA");
        var participantsSection = prompt.Substring(prompt.IndexOf("## Participantes", estructuraIndex));
        var participantsEnd = participantsSection.IndexOf("## Resumen Ejecutivo");
        var participantsText = participantsSection.Substring(0, participantsEnd);

        Assert.Contains("Aritz Villodas", participantsText);
        Assert.Contains("Marta Sánchez", participantsText);
        Assert.DoesNotContain("SPEAKER_00", participantsText);
        Assert.DoesNotContain("SPEAKER_01", participantsText);

        // 2. Directrices contienen reglas para documentar SPEAKER_XX de forma impersonal
        Assert.Contains("DIRECTRICES DE ATRIBUCIÓN DE PARTICIPANTES Y TRATAMIENTO DE LOCUTORES", prompt);
        Assert.Contains("se ha comentado que...", prompt);
        Assert.Contains("FORMA IMPERSONAL", prompt);
        Assert.Contains("NUNCA debe aparecer la palabra 'SPEAKER_XX'", prompt);
    }

    [Fact]
    public void SanitizeActaMarkdown_RemovesTechnicalSpeakersAndTransformsSpeakerVerbsToImpersonal()
    {
        // Arrange
        var meeting = new Meeting
        {
            Title = "Sincronización Técnica"
        };
        meeting.ExpectedParticipants.Add("Laura Gómez");

        var rawAiMarkdown = @"# Acta de la Reunión
## Información General
- Título: Sincronización Técnica

## Participantes
- Laura Gómez
- SPEAKER_00
- speaker_01

## Resumen Ejecutivo
SPEAKER_00 ha comentado que la infraestructura está lista para producción. Laura Gómez propuso iniciar el despliegue el lunes.
Más tarde, SPEAKER_01 indicó que se necesitaban pruebas adicionales.

## Acciones Acordadas
| Acción | Responsable | Fecha Límite |
|---|---|---|
| Despliegue en staging | Laura Gómez | 01/10/2026 |
| Pruebas de carga | SPEAKER_00 | 02/10/2026 |
";

        var options = new ActaGenerationOptions
        {
            Language = LanguageMode.Spanish,
            IsImpersonal = false
        };

        // Act
        var sanitized = OpenRouterActaService.SanitizeActaMarkdown(rawAiMarkdown, meeting, options);

        // Assert
        // 1. Participantes técnicos eliminados de la cabecera
        var participantsSection = sanitized.Substring(sanitized.IndexOf("## Participantes"));
        var participantsEnd = participantsSection.IndexOf("## Resumen Ejecutivo");
        var participantsText = participantsSection.Substring(0, participantsEnd);

        Assert.Contains("Laura Gómez", participantsText);
        Assert.DoesNotContain("SPEAKER_00", participantsText);
        Assert.DoesNotContain("speaker_01", participantsText);

        // 2. En el cuerpo, SPEAKER_00 ha comentado -> Se ha comentado
        Assert.Contains("Se ha comentado que la infraestructura está lista", sanitized);
        Assert.DoesNotContain("SPEAKER_00 ha comentado", sanitized);

        // 3. Intervención con Laura Gómez se preserva
        Assert.Contains("Laura Gómez propuso iniciar el despliegue el lunes", sanitized);

        // 4. speaker_01 indicó -> se indicó
        Assert.Contains("se indicó que se necesitaban pruebas adicionales", sanitized);
        Assert.DoesNotContain("speaker_01 indicó", sanitized);

        // 5. En tabla de acciones, SPEAKER_00 -> Equipo
        Assert.Contains("| Pruebas de carga | Equipo | 02/10/2026 |", sanitized);
        Assert.DoesNotContain("| SPEAKER_00 |", sanitized);
    }

    [Fact]
    public void Meeting_GetUserParticipants_CombinesExpectedAndRenamedSpeakersExcludingTechnicalCodes()
    {
        // Arrange
        var meeting = new Meeting();
        meeting.ExpectedParticipants.Add("Aritz");
        meeting.ExpectedParticipants.Add("Carlos");

        // Hablante ya renombrado con mismo nombre
        meeting.Participants.Add(new Speaker { Id = "SPEAKER_00", DisplayName = "Aritz" });
        // Hablante técnico no renombrado
        meeting.Participants.Add(new Speaker { Id = "SPEAKER_01", DisplayName = "SPEAKER_01" });
        // Hablante renombrado a nuevo nombre real
        meeting.Participants.Add(new Speaker { Id = "SPEAKER_02", DisplayName = "Laura" });

        // Act
        var userParticipants = meeting.GetUserParticipants();

        // Assert
        Assert.Equal(3, userParticipants.Count);
        Assert.Contains("Aritz", userParticipants);
        Assert.Contains("Carlos", userParticipants);
        Assert.Contains("Laura", userParticipants);
        Assert.DoesNotContain("SPEAKER_01", userParticipants);
    }
}
