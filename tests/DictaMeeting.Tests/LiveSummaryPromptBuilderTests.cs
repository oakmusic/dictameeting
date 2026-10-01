using DictaMeeting.AI.Services;
using Xunit;

namespace DictaMeeting.Tests;

public class LiveSummaryPromptBuilderTests
{
    [Fact]
    public void BuildPrompt_Spanish_ContainsStrictRulesAndChatMLTags()
    {
        string prevSummary = "Se está evaluando el nuevo sistema de cámaras.";
        string newTranscript = "Aritz: Planteamos usar seis unidades y procesar el vídeo en local.";

        string prompt = LiveSummaryPromptBuilder.BuildPrompt(prevSummary, newTranscript, "Spanish");

        Assert.Contains("<|im_start|>system", prompt);
        Assert.Contains("REGLAS CRÍTICAS:", prompt);
        Assert.Contains("Prohibido repetir", prompt);
        Assert.Contains("50 palabras", prompt);
        Assert.Contains("<|im_end|>", prompt);
        Assert.Contains("<|im_start|>user", prompt);
        Assert.Contains("TRANSCRIPCIÓN DEL TRAMO DE LA REUNIÓN:", prompt);
        Assert.Contains(newTranscript, prompt);
        Assert.Contains("<|im_start|>assistant", prompt);
    }

    [Fact]
    public void BuildPrompt_English_ContainsEnglishInstructions()
    {
        string prevSummary = "The team is evaluating the camera architecture.";
        string newTranscript = "John: We should deploy six cameras with on-prem processing.";

        string prompt = LiveSummaryPromptBuilder.BuildPrompt(prevSummary, newTranscript, "English");

        Assert.Contains("CRITICAL RULES:", prompt);
        Assert.Contains("50 words", prompt);
        Assert.Contains("SECTION TRANSCRIPT:", prompt);
        Assert.Contains(newTranscript, prompt);
    }

    [Fact]
    public void BuildPrompt_EmptyPreviousSummary_UsesInitialNotice()
    {
        string promptEs = LiveSummaryPromptBuilder.BuildPrompt("", "Interlocutor: Hola a todos.", "Spanish");
        Assert.Contains("TRANSCRIPCIÓN DEL TRAMO DE LA REUNIÓN:", promptEs);

        string promptEn = LiveSummaryPromptBuilder.BuildPrompt("   ", "Speaker: Hello everyone.", "English");
        Assert.Contains("SECTION TRANSCRIPT:", promptEn);
    }

    [Fact]
    public void BuildPrompt_TechnicalPreservation_ContainsRuleAgainstTranslatingTechTerms()
    {
        string prompt = LiveSummaryPromptBuilder.BuildPrompt("Resumen", "Usamos Docker, Kubernetes y ONNX Runtime.", "Spanish");
        Assert.Contains("nombres técnicos", prompt);
    }

    [Theory]
    [InlineData("En esta reunión se ha hablado de migrar la base de datos a PostgreSQL.", "migrar la base de datos a PostgreSQL.")]
    [InlineData("En esta reunión los participantes comentan que se pospone el lanzamiento.", "se pospone el lanzamiento.")]
    [InlineData("Resumen: El equipo ha acordado el presupuesto trimestral.", "El equipo ha acordado el presupuesto trimestral.")]
    [InlineData("Summary: The architecture was approved.", "The architecture was approved.")]
    [InlineData("\"Se aprueba el presupuesto para el despliegue local.\"", "Se aprueba el presupuesto para el despliegue local.")]
    [InlineData("«Se acuerda validar el modelo Qwen en CPU.»", "Se acuerda validar el modelo Qwen en CPU.")]
    public void SanitizeSummaryOutput_RemovesFluffPreambleAndQuotes(string raw, string expectedClean)
    {
        string sanitized = LiveSummaryPromptBuilder.SanitizeSummaryOutput(raw, "Resumen anterior");
        Assert.Equal(expectedClean, sanitized);
    }

    [Fact]
    public void SanitizeSummaryOutput_StripsSpecialTokensAndMarkdown()
    {
        string raw = "```\nSe revisan los logs del microservicio y se corrigen tres incidencias críticas.\n```<|im_end|>";
        string sanitized = LiveSummaryPromptBuilder.SanitizeSummaryOutput(raw, "Resumen previo");

        Assert.DoesNotContain("```", sanitized);
        Assert.DoesNotContain("<|im_end|>", sanitized);
        Assert.Contains("Se revisan los logs del microservicio", sanitized);
    }

    [Fact]
    public void SanitizeSummaryOutput_WhenEmptyOrTooShort_RetainsPreviousSummary()
    {
        string prev = "Se está configurando el clúster de servidores.";
        string sanitizedEmpty = LiveSummaryPromptBuilder.SanitizeSummaryOutput("", prev);
        string sanitizedShort = LiveSummaryPromptBuilder.SanitizeSummaryOutput("Ok.", prev);

        Assert.Equal(prev, sanitizedEmpty);
        Assert.Equal(prev, sanitizedShort);
    }

    [Fact]
    public void BuildPrompt_Spanish_DoesNotReferenceDictaMeetingAsAssistantPersona()
    {
        string prompt = LiveSummaryPromptBuilder.BuildPrompt("", "Transcripción de prueba", "Spanish");

        Assert.DoesNotContain("asistente de resumen en vivo de DictaMeeting", prompt);
        Assert.Contains("Prohibido mencionar a \"DictaMeeting\"", prompt);
    }

    [Fact]
    public void BuildPrompt_English_DoesNotReferenceDictaMeetingAsAssistantPersona()
    {
        string prompt = LiveSummaryPromptBuilder.BuildPrompt("", "Test transcript", "English");

        Assert.DoesNotContain("DictaMeeting's live meeting summary assistant", prompt);
        Assert.Contains("Strictly never mention \"DictaMeeting\"", prompt);
    }

    [Fact]
    public void SanitizeSummaryOutput_RemovesHallucinatedDictaMeetingReference()
    {
        string raw = "Esto es una absoluta barbaridad y afecta directamente a las cuentas de los trabajadores, incluyendo los de los asistentes a DictaMeeting.";
        string sanitized = LiveSummaryPromptBuilder.SanitizeSummaryOutput(raw, "");

        Assert.Equal("Esto es una absoluta barbaridad y afecta directamente a las cuentas de los trabajadores.", sanitized);
        Assert.DoesNotContain("DictaMeeting", sanitized);
    }

    [Fact]
    public void SanitizeSummaryOutput_TrimsTruncatedTrailingFragmentWhenPreviousSentenceComplete()
    {
        string raw = "Actualmente el precio de OPUS 5.5 es de 4 dólares por millón de tokens, lo que representa un ahorro de 15 veces. Además, la cantidad de tokens que se utilizan para generar documentos, presentaciones o pedir acceso al ordenador, se multip";
        string sanitized = LiveSummaryPromptBuilder.SanitizeSummaryOutput(raw, "");

        Assert.Equal("Actualmente el precio de OPUS 5.5 es de 4 dólares por millón de tokens, lo que representa un ahorro de 15 veces.", sanitized);
    }

    [Fact]
    public void SanitizeSummaryOutput_CleansDanglingConnectorAndCompletesSentence()
    {
        string raw = "Se ha indicado que la IA tendrá un costo, pero";
        string sanitized = LiveSummaryPromptBuilder.SanitizeSummaryOutput(raw, "");

        Assert.Equal("Se ha indicado que la IA tendrá un costo.", sanitized);
    }

    [Fact]
    public void SanitizeSummaryOutput_CleansTrailingDanglingPrepositionSequence()
    {
        string raw = "Además, la inteligencia artificial permite hacer más uso de ella, lo que lleva a un aumento en el consumo de recursos. Por lo tanto, aunque los precios continúan bajando, el gasto total sigue siendo mayor debido a la";
        string sanitized = LiveSummaryPromptBuilder.SanitizeSummaryOutput(raw, "");

        Assert.Equal("Además, la inteligencia artificial permite hacer más uso de ella, lo que lleva a un aumento en el consumo de recursos.", sanitized);
    }

    [Fact]
    public void SanitizeSummaryOutput_DiscardsIncompleteTrailingSentenceWhenEarlierSentenceExists()
    {
        string raw = "Se ha presentado la idea de que la inteligencia artificial será fundamental para el mundo en los próximos dos años, y se ha preguntado si será la mejor o la única necesaria para el futuro. Se ha señalado que la competencia puede suponer un problema, ya que otras empresas pueden utilizar inteligencias";
        string sanitized = LiveSummaryPromptBuilder.SanitizeSummaryOutput(raw, "");

        Assert.Equal("Se ha presentado la idea de que la inteligencia artificial será fundamental para el mundo en los próximos dos años, y se ha preguntado si será la mejor o la única necesaria para el futuro.", sanitized);
    }
}
