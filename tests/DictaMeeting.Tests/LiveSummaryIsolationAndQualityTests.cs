using DictaMeeting.AI.Interfaces;
using DictaMeeting.AI.Models;
using DictaMeeting.AI.Services;
using DictaMeeting.Meetings.Models;
using Xunit;

namespace DictaMeeting.Tests;

public class LiveSummaryIsolationAndQualityTests
{
    private class TestSummaryService : ILiveSummaryService
    {
        public int GenerateCount { get; private set; }
        public bool IsGenerating { get; set; }
        public bool IsModelLoaded { get; set; } = true;
        public SummaryMetrics? LastMetrics { get; set; }
        public Func<string, string, string, CancellationToken, Task<string>>? CustomGenerator { get; set; }
        public bool WasCancelled { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task<string> GenerateSummaryAsync(
            string previousSummary,
            string newTranscriptWindow,
            string language = "Spanish",
            CancellationToken cancellationToken = default)
        {
            GenerateCount++;
            if (CustomGenerator != null)
            {
                return await CustomGenerator(previousSummary, newTranscriptWindow, language, cancellationToken);
            }

            return $"Resumen del fragmento tratado: {newTranscriptWindow.Trim()}";
        }

        public void CancelCurrentGeneration()
        {
            WasCancelled = true;
        }

        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private class TestModelManager : ILiveSummaryModelManager
    {
        public string ModelsDirectory { get; private set; } = "C:\\test\\models\\summary";
        public void SetModelsDirectory(string path) => ModelsDirectory = path;
        public bool ModelDownloaded { get; set; } = true;
        public string ModelName => "Qwen2.5-1.5B-Instruct";
        public string Quantization => "Q4_K_M";
        public long ModelSizeBytes => 986L * 1024 * 1024;
        public string LocalModelPath => "dummy/path/model.gguf";
        public string GetModelPath() => LocalModelPath;
        public bool IsModelDownloaded() => ModelDownloaded;
        public Task<string> EnsureModelDownloadedAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(LocalModelPath);
        public bool DeleteModel() => true;
    }

    // 1. Nueva reunión no hereda ningún resumen de la reunión anterior.
    [Fact]
    public async Task NewMeeting_DoesNotInheritAnySummary_FromPreviousMeeting()
    {
        var service = new TestSummaryService();
        var manager = new TestModelManager();
        var coordinator = new LiveSummaryCoordinator(service, manager);

        // Reunión 1
        coordinator.Start("meeting-1", "Spanish");
        coordinator.AddSegment(new TranscriptSegment
        {
            Id = "1",
            SpeakerDisplayName = "Laura",
            StartTime = TimeSpan.Zero,
            EndTime = TimeSpan.FromSeconds(25),
            Text = "Discutimos las mejoras de comunicación y herramientas para el equipo de trabajo."
        });
        await coordinator.TriggerImmediateSummaryAsync();

        Assert.Single(coordinator.SummaryCards);
        Assert.NotEmpty(coordinator.CurrentSummary);

        // Iniciar Reunión 2
        coordinator.Start("meeting-2", "Spanish");

        Assert.Equal("meeting-2", coordinator.CurrentMeetingId);
        Assert.Empty(coordinator.SummaryCards);
        Assert.Equal(string.Empty, coordinator.CurrentSummary);
        Assert.Null(coordinator.LastUpdatedTime);

        coordinator.Stop();
    }

    // 2. Una respuesta de una reunión anterior no puede insertarse en la reunión actual.
    [Fact]
    public async Task ResponseFromPreviousMeeting_CannotBeInsertedIntoCurrentMeeting()
    {
        var service = new TestSummaryService();
        var manager = new TestModelManager();
        var tcs = new TaskCompletionSource<string>();

        service.CustomGenerator = async (prev, win, lang, ct) =>
        {
            return await tcs.Task;
        };

        var coordinator = new LiveSummaryCoordinator(service, manager);
        coordinator.Start("meeting-1", "Spanish");

        coordinator.AddSegment(new TranscriptSegment
        {
            Id = "1",
            SpeakerDisplayName = "Carlos",
            StartTime = TimeSpan.Zero,
            EndTime = TimeSpan.FromSeconds(20),
            Text = "Texto de la reunión uno sobre presupuestos y finanzas del departamento."
        });

        // Lanzar inferencia asíncrona para reunión 1
        var pendingTask = coordinator.TriggerImmediateSummaryAsync();

        // Antes de que termine la inferencia, se crea e inicia una nueva reunión 2
        coordinator.Start("meeting-2", "Spanish");

        // Completar la inferencia que pertenecía a la reunión 1
        tcs.SetResult("Resumen de finanzas de la reunión uno.");
        await pendingTask;

        // La reunión 2 NO debe haber recibido la tarjeta de la reunión 1
        Assert.Empty(coordinator.SummaryCards);
        Assert.Equal(string.Empty, coordinator.CurrentSummary);
        Assert.Equal("meeting-2", coordinator.CurrentMeetingId);

        coordinator.Stop();
    }

    // 3. Reset de Live Summary limpia completamente el estado necesario.
    [Fact]
    public async Task Reset_CleansCompleteSummaryState()
    {
        var service = new TestSummaryService();
        var manager = new TestModelManager();
        var coordinator = new LiveSummaryCoordinator(service, manager);

        coordinator.Start("meeting-reset-test", "Spanish");
        coordinator.AddSegment(new TranscriptSegment
        {
            Id = "1",
            SpeakerDisplayName = "Marta",
            StartTime = TimeSpan.Zero,
            EndTime = TimeSpan.FromSeconds(30),
            Text = "Segmento para probar la limpieza absoluta y reset del estado del coordinador."
        });
        await coordinator.TriggerImmediateSummaryAsync();

        Assert.NotEmpty(coordinator.SummaryCards);
        Assert.True(coordinator.IsRunning);

        coordinator.Reset();

        Assert.False(coordinator.IsRunning);
        Assert.False(coordinator.IsGenerating);
        Assert.Null(coordinator.CurrentMeetingId);
        Assert.Empty(coordinator.SummaryCards);
        Assert.Equal(string.Empty, coordinator.CurrentSummary);
        Assert.Null(coordinator.LastUpdatedTime);
    }

    // 4. Transcript vacío/no suficiente => no se genera tarjeta.
    [Fact]
    public async Task EmptyOrInsufficientTranscript_DoesNotGenerateCard()
    {
        var service = new TestSummaryService();
        var manager = new TestModelManager();
        var coordinator = new LiveSummaryCoordinator(service, manager);

        coordinator.Start("meeting-empty-test", "Spanish");

        // Añadir monosílabo insuficiente
        coordinator.AddSegment(new TranscriptSegment
        {
            Id = "1",
            SpeakerDisplayName = "Aritz",
            StartTime = TimeSpan.Zero,
            EndTime = TimeSpan.FromSeconds(5),
            Text = "Ok. Sí."
        });

        await coordinator.TriggerImmediateSummaryAsync();

        Assert.Equal(0, service.GenerateCount);
        Assert.Empty(coordinator.SummaryCards);

        coordinator.Stop();
    }

    // 5. Un output con una frase repetida muchas veces es detectado como inválido.
    [Fact]
    public void OutputWithRepeatedSentence_DetectedAsInvalid()
    {
        var validator = new LiveSummaryOutputValidator();

        string transcript = "The teacher explained that the object pronoun replaces a noun in the sentence.";
        string degenerateOutput =
            "Se ha explicado que el pronombre es un sustantivo. " +
            "Se ha explicado que el pronombre objeto es un pronombre objeto. " +
            "Se ha explicado que el pronombre objeto es un pronombre objeto. " +
            "Se ha explicado que el pronombre objeto es un pronombre objeto.";

        var result = validator.ValidateOutput(
            degenerateOutput,
            transcript,
            previousCardText: null,
            cardStartTime: TimeSpan.FromMinutes(35),
            cardEndTime: TimeSpan.FromMinutes(37),
            lastSummarizedEndTime: TimeSpan.FromMinutes(34));

        Assert.False(result.IsValid);
        Assert.Equal(LiveSummaryValidationFailureReason.RepeatedSentence, result.FailureReason);
        Assert.Contains("Frase repetida", result.Details);
    }

    // 6. Un output excesivamente largo es detectado.
    [Fact]
    public void ExcessivelyLongOutput_DetectedAsInvalid()
    {
        var validator = new LiveSummaryOutputValidator();

        string transcript = "Hablamos del nuevo plan de despliegue en la infraestructura de la empresa.";
        // Generar un párrafo extenso de más de 100 palabras sin frases repetidas
        string longOutput =
            "Se ha acordado avanzar con la planificación técnica del proyecto de despliegue continuo. " +
            "Los ingenieros han presentado una propuesta integral para optimizar los contenedores y los servicios en la nube. " +
            "Se ha detallado el cronograma para las pruebas de carga y rendimiento de la base de datos relacional. " +
            "El equipo de operaciones ha confirmado que la infraestructura de servidores estará disponible la próxima semana. " +
            "Se han evaluado los costes asociados a las licencias de software y los contratos con proveedores externos. " +
            "La dirección ha solicitado una revisión adicional de seguridad informática antes del lanzamiento comercial. " +
            "Los responsables de producto han acordado incorporar las peticiones prioritarias de los clientes clave.";

        var result = validator.ValidateOutput(
            longOutput,
            transcript,
            previousCardText: null,
            cardStartTime: TimeSpan.FromSeconds(0),
            cardEndTime: TimeSpan.FromSeconds(60),
            lastSummarizedEndTime: TimeSpan.Zero);

        Assert.False(result.IsValid);
        Assert.Equal(LiveSummaryValidationFailureReason.ExcessiveLength, result.FailureReason);
    }

    // 7. Un output casi idéntico a la tarjeta anterior es detectado.
    [Fact]
    public void OutputAlmostIdenticalToPreviousCard_DetectedAsInvalid()
    {
        var validator = new LiveSummaryOutputValidator();

        string previousCard = "Se ha revisado el despliegue del clúster de servidores en la nube privada con Docker y Kubernetes.";
        string newDuplicate = "Se ha revisado el despliegue del clúster de servidores en la nube privada con Docker y Kubernetes hoy.";

        var result = validator.ValidateOutput(
            newDuplicate,
            "Nueva discusión sobre escalado.",
            previousCardText: previousCard,
            cardStartTime: TimeSpan.FromSeconds(60),
            cardEndTime: TimeSpan.FromSeconds(120),
            lastSummarizedEndTime: TimeSpan.FromSeconds(60));

        Assert.False(result.IsValid);
        Assert.Equal(LiveSummaryValidationFailureReason.DuplicateOfPreviousCard, result.FailureReason);
    }

    // 8. Las ventanas de transcript no provocan procesamiento duplicado.
    [Fact]
    public async Task TranscriptWindows_DoNotCauseDuplicateProcessing()
    {
        var service = new TestSummaryService();
        var manager = new TestModelManager();
        var coordinator = new LiveSummaryCoordinator(service, manager);

        coordinator.Start("window-test", "Spanish");

        coordinator.AddSegment(new TranscriptSegment
        {
            Id = "1",
            StartTime = TimeSpan.Zero,
            EndTime = TimeSpan.FromSeconds(30),
            Text = "Primera discusión sobre los requerimientos funcionales del sistema."
        });

        await coordinator.TriggerImmediateSummaryAsync();
        Assert.Equal(1, service.GenerateCount);
        Assert.Single(coordinator.SummaryCards);
        Assert.Equal(TimeSpan.FromSeconds(30), coordinator.SummaryCards[0].EndTime);

        // Sin añadir nuevos segmentos, un segundo disparo no debe generar tarjeta duplicada
        await coordinator.TriggerImmediateSummaryAsync();
        Assert.Equal(1, service.GenerateCount);
        Assert.Single(coordinator.SummaryCards);

        // Al añadir nuevo segmento posterior a 30s
        coordinator.AddSegment(new TranscriptSegment
        {
            Id = "2",
            StartTime = TimeSpan.FromSeconds(30),
            EndTime = TimeSpan.FromSeconds(60),
            Text = "Segunda discusión sobre la base de datos distribuida y caché Redis."
        });

        await coordinator.TriggerImmediateSummaryAsync();
        Assert.Equal(2, service.GenerateCount);
        Assert.Equal(2, coordinator.SummaryCards.Count);
        Assert.Equal(TimeSpan.FromSeconds(30), coordinator.SummaryCards[1].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(60), coordinator.SummaryCards[1].EndTime);

        coordinator.Stop();
    }

    // 9. Un rango temporal ya procesado no vuelve a crear la misma tarjeta.
    [Fact]
    public void AlreadyProcessedTimeRange_DoesNotRecreateSameCard()
    {
        var validator = new LiveSummaryOutputValidator();

        var result = validator.ValidateOutput(
            "Resumen sobre la infraestructura tecnológica.",
            "Infraestructura y servidores en la nube.",
            previousCardText: null,
            cardStartTime: TimeSpan.FromSeconds(10),
            cardEndTime: TimeSpan.FromSeconds(30),
            lastSummarizedEndTime: TimeSpan.FromSeconds(30)); // Fin ya alcanzado previamente

        Assert.False(result.IsValid);
        Assert.Equal(LiveSummaryValidationFailureReason.InvalidTimeRange, result.FailureReason);
    }

    // 10. Las tareas async/cancelaciones no pueden escribir resultados de una reunión antigua sobre una reunión nueva.
    [Fact]
    public async Task AsyncCancellation_CannotWriteOldResultsOntoNewMeeting()
    {
        var service = new TestSummaryService();
        var manager = new TestModelManager();
        var tcs = new TaskCompletionSource<string>();
        string capturedMeetingIdOnEvent = string.Empty;

        service.CustomGenerator = async (prev, win, lang, ct) =>
        {
            return await tcs.Task;
        };

        var coordinator = new LiveSummaryCoordinator(service, manager);
        coordinator.SummaryUpdated += (sender, args) =>
        {
            capturedMeetingIdOnEvent = args.MeetingId;
        };

        coordinator.Start("old-meeting-id", "Spanish");
        coordinator.AddSegment(new TranscriptSegment
        {
            Id = "seg-1",
            StartTime = TimeSpan.Zero,
            EndTime = TimeSpan.FromSeconds(25),
            Text = "Conversación de la reunión vieja sobre ventas comerciales del trimestre."
        });

        // Lanzar tarea de generación en background
        var runTask = coordinator.TriggerImmediateSummaryAsync();

        // El usuario cancela/detiene la reunión vieja e inicia una reunión nueva
        coordinator.Stop();
        coordinator.Start("new-meeting-id", "Spanish");

        // La inferencia de la reunión vieja finaliza tardíamente
        tcs.SetResult("Resumen de ventas comerciales de la reunión vieja.");
        await runTask;

        // La reunión nueva debe estar completamente limpia
        Assert.Empty(coordinator.SummaryCards);
        Assert.Equal(string.Empty, coordinator.CurrentSummary);
        Assert.NotEqual("old-meeting-id", capturedMeetingIdOnEvent);

        coordinator.Stop();
    }

    // REGRESIÓN: Caso de prueba de la clase de inglés (BUG 1)
    [Fact]
    public void Regression_EnglishLesson_RejectsUnrelatedPreviousMeetingText()
    {
        var validator = new LiveSummaryOutputValidator();

        string englishTranscript = """
            And um. Let me share that with you now. Can we all see the screen?
            Yes, no yes, no okay, that's okay. Let's go through the classroom rules.
            Welcome everybody. Welcome to today's group lesson.
            Make sure to be in a quiet place, free of distractions. Please keep your microphones on mute to avoid any interruptions.
            """;

        // Texto contaminado de la reunión anterior (exacto del BUG 1)
        string contaminatedOldSummary =
            "Se ha comentado que se necesita mejorar la comunicación en el equipo, " +
            "se ha explicado la importancia de la colaboración y se ha acordado la " +
            "implementación de nuevas herramientas tecnológicas para facilitar la colaboración.";

        var result = validator.ValidateOutput(
            contaminatedOldSummary,
            englishTranscript,
            previousCardText: null,
            cardStartTime: TimeSpan.Zero,
            cardEndTime: TimeSpan.FromSeconds(33),
            lastSummarizedEndTime: TimeSpan.Zero);

        // Debe ser detectado y rechazado por desconexión semántica completa
        Assert.False(result.IsValid);
        Assert.Equal(LiveSummaryValidationFailureReason.SemanticDisconnect, result.FailureReason);
    }

    // REGRESIÓN: Caso de prueba de bucle degenerativo de pronombre objeto (BUG 2)
    [Fact]
    public void Regression_ObjectPronounRepetitionLoop_RejectedByValidator()
    {
        var validator = new LiveSummaryOutputValidator();

        string transcript = """
            So in this example, the pronoun replaces the noun. We are looking at object pronouns.
            The object pronoun receives the action in the sentence.
            """;

        // Texto exacto reportado en la captura del BUG 2
        string loopSummary =
            "Se ha comentado que en el ejemplo se menciona \"fawning something,\" pero se ve que se usa en el texto. " +
            "Se ha explicado que el pronombre es una sustantivo que puede ser reemplazado por un nombre. " +
            "Se ha explicado que el pronombre es un pronombre objeto. " +
            "Se ha explicado que el pronombre objeto se usa para reemplazar el nombre. " +
            "Se ha explicado que el pronombre objeto es un pronombre objeto. " +
            "Se ha explicado que el pronombre objeto es un pronombre objeto. " +
            "Se ha explicado que el pronombre objeto es un pronombre objeto. " +
            "Se ha explicado que el pronombre objeto es un pronombre objeto.";

        var result = validator.ValidateOutput(
            loopSummary,
            transcript,
            previousCardText: null,
            cardStartTime: TimeSpan.FromMinutes(35),
            cardEndTime: TimeSpan.FromMinutes(37),
            lastSummarizedEndTime: TimeSpan.FromMinutes(35));

        Assert.False(result.IsValid);
        Assert.True(
            result.FailureReason == LiveSummaryValidationFailureReason.RepeatedSentence ||
            result.FailureReason == LiveSummaryValidationFailureReason.DegenerativeLoop);
    }

    // REGRESIÓN: Texto válido de la clase de inglés es aceptado
    [Fact]
    public void Regression_ValidEnglishClassSummary_AcceptedByValidator()
    {
        var validator = new LiveSummaryOutputValidator();

        string englishTranscript = """
            Let's go through the classroom rules. Welcome to today's group lesson.
            Make sure to be in a quiet place, free of distractions. Please keep your microphones on mute to avoid any interruptions.
            """;

        string validSummary =
            "Se ha explicado que los participantes deben ubicarse en un espacio tranquilo sin distracciones " +
            "y mantener los micrófonos silenciados para evitar interrupciones durante la lección grupal.";

        var result = validator.ValidateOutput(
            validSummary,
            englishTranscript,
            previousCardText: null,
            cardStartTime: TimeSpan.FromSeconds(33),
            cardEndTime: TimeSpan.FromSeconds(95),
            lastSummarizedEndTime: TimeSpan.FromSeconds(33));

        Assert.True(result.IsValid);
    }
}
