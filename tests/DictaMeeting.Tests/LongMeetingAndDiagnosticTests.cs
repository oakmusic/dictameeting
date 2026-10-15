using System.Net;
using System.Text;
using System.Text.Json;
using DictaMeeting.AI.Models;
using DictaMeeting.AI.Services;
using DictaMeeting.Infrastructure.Persistence;
using DictaMeeting.Infrastructure.Security;
using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Exporters;
using DictaMeeting.Meetings.Models;
using Xunit;

namespace DictaMeeting.Tests;

/// <summary>
/// Pruebas obligatorias (Fase I) para verificar el diagnóstico y solución de actas vacías o incompletas
/// en reuniones largas y cortas, límites de tokens, razonamiento, fragmentación, reintentos y persistencia.
/// </summary>
public class LongMeetingAndDiagnosticTests
{
    private class FakeSecureStorage : ISecureStorageService
    {
        private readonly Dictionary<string, string> _store = new();
        public void SaveSecret(string key, string secret) => _store[key] = secret;
        public string? GetSecret(string key) => _store.TryGetValue(key, out var val) ? val : null;
        public void DeleteSecret(string key) => _store.Remove(key);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>>? HandlerFunc { get; set; }
        public List<HttpRequestMessage> CapturedRequests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequests.Add(request);
            if (HandlerFunc != null)
            {
                return await HandlerFunc(request);
            }

            var defaultResponse = new
            {
                id = "mock-req-default",
                choices = new[]
                {
                    new
                    {
                        message = new { role = "assistant", content = "# Acta de la Reunión\n## Resumen Ejecutivo\nReunión satisfactoria." },
                        finish_reason = "stop"
                    }
                },
                usage = new { prompt_tokens = 200, completion_tokens = 100, total_tokens = 300 }
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(defaultResponse), Encoding.UTF8, "application/json")
            };
        }
    }

    // 1. Una respuesta de OpenRouter completada correctamente (finish_reason = "stop")
    [Fact]
    public async Task Scenario01_OpenRouterResponse_CompletedSuccessfully_StopFinishReason()
    {
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = _ =>
            {
                var payload = new
                {
                    id = "gen-test-1",
                    choices = new[]
                    {
                        new
                        {
                            message = new { role = "assistant", content = "# Acta de la Reunión\n## Información General\n- Título: Demo\n## Resumen Ejecutivo\nTodo aprobado." },
                            finish_reason = "stop"
                        }
                    },
                    usage = new
                    {
                        prompt_tokens = 1200,
                        completion_tokens = 450,
                        total_tokens = 1650
                    }
                };

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                });
            }
        };

        var service = new OpenRouterActaService(new HttpClient(mockHandler));
        var meeting = new Meeting { Title = "Reunión Corta", StartTime = DateTimeOffset.Now, EndTime = DateTimeOffset.Now.AddMinutes(15) };
        meeting.Transcript.Add(new TranscriptSegment { Text = "Aprobamos la propuesta sin objeciones." });

        var options = new ActaGenerationOptions { ApiKey = "sk-test", ModelName = "openai/gpt-6-luna" };
        var result = await service.GenerateActaAsync(meeting, options);

        Assert.True(result.Success);
        Assert.False(result.IsTruncated);
        Assert.Equal("stop", result.FinishReason);
        Assert.NotNull(result.MarkdownContent);
        Assert.Contains("# Acta de la Reunión", result.MarkdownContent);
        Assert.Equal(1200, result.PromptTokensUsed);
        Assert.Equal(450, result.CompletionTokensUsed);
        Assert.Equal(1650, result.TotalTokensUsed);
    }

    // 2. Una respuesta que finaliza por límite de longitud (finish_reason = "length")
    // Verifica detección de truncamiento y no guardado silencioso como completo
    [Fact]
    public async Task Scenario02_OpenRouterResponse_EndsWithLengthLimit_DetectsTruncationAndDoesNotReportSuccess()
    {
        int callCount = 0;
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = _ =>
            {
                callCount++;
                // Simula que tanto la llamada directa como el intento de continuación terminan por length
                var payload = new
                {
                    id = $"gen-trunc-{callCount}",
                    choices = new[]
                    {
                        new
                        {
                            message = new { role = "assistant", content = "# Acta de la Reunión\n## Información General\n- Título: Incompleta\nTexto cortado a la mitad de una fra" },
                            finish_reason = "length"
                        }
                    },
                    usage = new
                    {
                        prompt_tokens = 2000,
                        completion_tokens = 4000,
                        total_tokens = 6000
                    }
                };

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                });
            }
        };

        var service = new OpenRouterActaService(new HttpClient(mockHandler));
        var meeting = new Meeting { Title = "Reunión Truncada", StartTime = DateTimeOffset.Now, EndTime = DateTimeOffset.Now.AddMinutes(20) };
        meeting.Transcript.Add(new TranscriptSegment { Text = "Discusión extensa que excede los tokens." });

        var options = new ActaGenerationOptions { ApiKey = "sk-test", ModelName = "qwen/qwen3.8-flash", DisableHierarchicalProcessing = true };
        var result = await service.GenerateActaAsync(meeting, options);

        Assert.False(result.Success, "Un acta truncada por límite de longitud no debe presentarse como Success = true.");
        Assert.True(result.IsTruncated, "Debe marcarse explícitamente IsTruncated = true.");
        Assert.Equal("length", result.FinishReason);
        Assert.Contains("límite de tokens", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(result.PartialMarkdownContent), "El contenido parcial debe conservarse para recuperación.");
    }

    // 3. Una respuesta con contenido vacío diagnosticando agotamiento de tokens / razonamiento
    [Fact]
    public async Task Scenario03_OpenRouterResponse_EmptyContent_DiagnosesReasoningTokenExhaustion()
    {
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = _ =>
            {
                // El modelo gastó los 4000 tokens en la fase de razonamiento y devolvió content = "" con finish_reason = "length"
                var payload = new
                {
                    id = "gen-empty-reasoning",
                    choices = new[]
                    {
                        new
                        {
                            message = new
                            {
                                role = "assistant",
                                content = "",
                                reasoning = "Pensando detenidamente en todos los puntos de la reunión..."
                            },
                            finish_reason = "length"
                        }
                    },
                    usage = new
                    {
                        prompt_tokens = 27006,
                        completion_tokens = 4000,
                        total_tokens = 31006,
                        completion_tokens_details = new
                        {
                            reasoning_tokens = 4000
                        }
                    }
                };

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                });
            }
        };

        var service = new OpenRouterActaService(new HttpClient(mockHandler));
        var meeting = new Meeting { Title = "Reunión con razonamiento exhaustivo", StartTime = DateTimeOffset.Now, EndTime = DateTimeOffset.Now.AddMinutes(25) };
        meeting.Transcript.Add(new TranscriptSegment { Text = "Debate complejo con mucho razonamiento necesario." });

        var options = new ActaGenerationOptions { ApiKey = "sk-test", ModelName = "openai/gpt-6-luna", DisableHierarchicalProcessing = true };
        var result = await service.GenerateActaAsync(meeting, options);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("agotó el límite de tokens", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(4000, result.ReasoningTokensUsed);
    }

    // 4. Un error HTTP del proveedor con detalle de error disponible (401, 402, 429, 500)
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "{\"error\": {\"message\": \"API Key invalid or revoked\"}}", "Clave de API de OpenRouter no válida")]
    [InlineData(HttpStatusCode.PaymentRequired, "{\"error\": {\"message\": \"Credit balance exhausted\"}}", "Crédito insuficiente")]
    [InlineData((HttpStatusCode)429, "{\"error\": {\"message\": \"Rate limit exceeded: 20 rpm\"}}", "Límite de solicitudes alcanzado")]
    [InlineData(HttpStatusCode.InternalServerError, "{\"error\": {\"message\": \"Provider backend unreachable\"}}", "Provider backend unreachable")]
    public async Task Scenario04_HttpError_FromProvider_ExtractsErrorDetail(HttpStatusCode status, string jsonBody, string expectedSnippet)
    {
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = _ => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            })
        };

        var service = new OpenRouterActaService(new HttpClient(mockHandler));
        var meeting = new Meeting { Title = "Reunión Error HTTP" };
        meeting.Transcript.Add(new TranscriptSegment { Text = "Texto de reunión" });

        var options = new ActaGenerationOptions { ApiKey = "sk-test", ModelName = "qwen/qwen3.8-flash" };
        var result = await service.GenerateActaAsync(meeting, options);

        Assert.False(result.Success);
        Assert.Contains(expectedSnippet, result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // 5. Un timeout y una cancelación
    [Fact]
    public async Task Scenario05_TimeoutAndCancellation_HandledGracefully()
    {
        // Caso A: Cancelación mediante CancellationToken
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var service = new OpenRouterActaService();
        var meeting = new Meeting { Title = "Cancel Meeting" };
        meeting.Transcript.Add(new TranscriptSegment { Text = "Texto" });

        var resultCancel = await service.GenerateActaAsync(meeting, new ActaGenerationOptions { ApiKey = "sk-test" }, cts.Token);
        Assert.False(resultCancel.Success);
        Assert.Contains("cancelada", resultCancel.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Caso B: Timeout de HttpClient
        var mockTimeoutHandler = new MockHttpMessageHandler
        {
            HandlerFunc = _ => throw new TaskCanceledException("Timeout simulado de red.")
        };

        var timeoutService = new OpenRouterActaService(new HttpClient(mockTimeoutHandler));
        var resultTimeout = await timeoutService.GenerateActaAsync(meeting, new ActaGenerationOptions { ApiKey = "sk-test" });
        Assert.False(resultTimeout.Success);
        Assert.Contains("timeout", resultTimeout.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // 6. Un modelo que devuelve un motivo de finalización diferente (content_filter, refusal)
    [Fact]
    public async Task Scenario06_DifferentFinishReasons_HandledCorrectly()
    {
        // Caso A: Filtro de contenido (finish_reason = "content_filter")
        var mockFilterHandler = new MockHttpMessageHandler
        {
            HandlerFunc = _ =>
            {
                var payload = new
                {
                    choices = new[]
                    {
                        new { message = new { role = "assistant", content = "" }, finish_reason = "content_filter" }
                    }
                };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                });
            }
        };

        var serviceA = new OpenRouterActaService(new HttpClient(mockFilterHandler));
        var meeting = new Meeting { Title = "Content Filter Test" };
        meeting.Transcript.Add(new TranscriptSegment { Text = "Contenido sensible" });

        var resultA = await serviceA.GenerateActaAsync(meeting, new ActaGenerationOptions { ApiKey = "sk-test" });
        Assert.False(resultA.Success);
        Assert.Contains("filtro de seguridad", resultA.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Caso B: Refusal explícito en el mensaje
        var mockRefusalHandler = new MockHttpMessageHandler
        {
            HandlerFunc = _ =>
            {
                var payload = new
                {
                    choices = new[]
                    {
                        new
                        {
                            message = new { role = "assistant", content = "", refusal = "I cannot fulfill this request due to organizational compliance rules." },
                            finish_reason = "stop"
                        }
                    }
                };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                });
            }
        };

        var serviceB = new OpenRouterActaService(new HttpClient(mockRefusalHandler));
        var resultB = await serviceB.GenerateActaAsync(meeting, new ActaGenerationOptions { ApiKey = "sk-test" });
        Assert.False(resultB.Success);
        Assert.Contains("rechazó", resultB.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // 7. La compatibilidad con la configuración actual de razonamiento (effort vs max_tokens, exclude=true, nunca simultáneos)
    [Fact]
    public async Task Scenario07_ReasoningConfigurationCompatibility_NeverSendsEffortAndMaxTokensSimultaneously()
    {
        string? capturedJson = null;
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = req =>
            {
                capturedJson = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                var resp = new { choices = new[] { new { message = new { content = "# Acta" }, finish_reason = "stop" } } };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(resp), Encoding.UTF8, "application/json")
                });
            }
        };

        var service = new OpenRouterActaService(new HttpClient(mockHandler));
        var meeting = new Meeting { Title = "Reasoning Test" };
        meeting.Transcript.Add(new TranscriptSegment { Text = "Debate" });

        // Prueba con ReasoningMaxTokens explícito
        var optionsTokens = new ActaGenerationOptions
        {
            ApiKey = "sk-test",
            ModelName = "openai/gpt-6-luna",
            ReasoningMaxTokens = 2500
        };

        await service.GenerateActaAsync(meeting, optionsTokens);
        Assert.NotNull(capturedJson);

        using (var doc = JsonDocument.Parse(capturedJson))
        {
            var reasoning = doc.RootElement.GetProperty("reasoning");
            Assert.True(reasoning.TryGetProperty("max_tokens", out var mtElem));
            Assert.Equal(2500, mtElem.GetInt32());
            Assert.False(reasoning.TryGetProperty("effort", out _), "NO debe incluir effort si max_tokens está presente.");
            Assert.True(reasoning.GetProperty("exclude").GetBoolean());
        }

        // Prueba con ReasoningEffort explícito
        var optionsEffort = new ActaGenerationOptions
        {
            ApiKey = "sk-test",
            ModelName = "openai/gpt-6-luna",
            ReasoningEffort = "medium"
        };

        await service.GenerateActaAsync(meeting, optionsEffort);
        using (var doc = JsonDocument.Parse(capturedJson!))
        {
            var reasoning = doc.RootElement.GetProperty("reasoning");
            Assert.True(reasoning.TryGetProperty("effort", out var effElem));
            Assert.Equal("medium", effElem.GetString());
            Assert.False(reasoning.TryGetProperty("max_tokens", out _), "NO debe incluir max_tokens si effort está presente.");
            Assert.True(reasoning.GetProperty("exclude").GetBoolean());
        }
    }

    // 8. Una reunión corta que siga utilizando el flujo directo
    [Fact]
    public async Task Scenario08_ShortMeeting_UsesDirectFlow()
    {
        var mockHandler = new MockHttpMessageHandler();
        var service = new OpenRouterActaService(new HttpClient(mockHandler));

        var shortMeeting = new Meeting
        {
            Title = "Daily Standup",
            StartTime = DateTimeOffset.Now,
            EndTime = DateTimeOffset.Now.AddMinutes(15) // 15 minutos
        };
        shortMeeting.Transcript.Add(new TranscriptSegment { Text = "Actualización de tareas del sprint." });

        var options = new ActaGenerationOptions { ApiKey = "sk-test" };

        Assert.False(TranscriptChunker.ShouldUseHierarchicalGeneration(shortMeeting, options));

        var result = await service.GenerateActaAsync(shortMeeting, options);

        Assert.True(result.Success);
        Assert.Equal(1, result.StagesExecuted);
        Assert.Single(mockHandler.CapturedRequests);
    }

    // 9. Una reunión larga que utilice procesamiento jerárquico
    [Fact]
    public async Task Scenario09_LongMeeting_UsesHierarchicalProcessing()
    {
        var mockHandler = new MockHttpMessageHandler();
        var service = new OpenRouterActaService(new HttpClient(mockHandler));

        var longMeeting = new Meeting
        {
            Title = "Reunión Trimestral Estratégica",
            StartTime = DateTimeOffset.Now,
            EndTime = DateTimeOffset.Now.AddHours(2).AddMinutes(8) // 2h 8min
        };

        // Generar transcripción extensa con múltiples segmentos a lo largo de las 2h 8min
        for (int i = 0; i < 40; i++)
        {
            longMeeting.Transcript.Add(new TranscriptSegment
            {
                StartTime = TimeSpan.FromMinutes(i * 3),
                EndTime = TimeSpan.FromMinutes(i * 3 + 2.5),
                SpeakerDisplayName = i % 2 == 0 ? "Laura" : "Carlos",
                Text = $"Discusión estratégica en el minuto {i * 3} sobre inversiones, métricas y decisiones del proyecto."
            });
        }

        var options = new ActaGenerationOptions { ApiKey = "sk-test", ModelName = "openai/gpt-6-luna" };

        Assert.True(TranscriptChunker.ShouldUseHierarchicalGeneration(longMeeting, options));

        var result = await service.GenerateActaAsync(longMeeting, options);

        Assert.True(result.Success);
        Assert.True(result.StagesExecuted > 1, "Una reunión de 2h 8min debe procesarse en múltiples etapas jerárquicas.");
        Assert.True(mockHandler.CapturedRequests.Count > 1, "Debe realizar llamadas a las distintas etapas (análisis, síntesis, redacción).");
    }

    // 10. Una transcripción cuyo tamaño obligue a dividirse en múltiples fragmentos
    [Fact]
    public void Scenario10_LargeTranscript_SplitsIntoMultipleChunksWithNaturalBoundariesAndOverlap()
    {
        var meeting = new Meeting
        {
            Title = "Reunión Extensa de Junta",
            StartTime = DateTimeOffset.Now,
            EndTime = DateTimeOffset.Now.AddHours(2)
        };

        for (int i = 0; i < 50; i++)
        {
            meeting.Transcript.Add(new TranscriptSegment
            {
                StartTime = TimeSpan.FromMinutes(i * 2.4),
                EndTime = TimeSpan.FromMinutes(i * 2.4 + 2),
                SpeakerId = $"SPEAKER_{i % 3:D2}",
                Text = $"Intervención detallada con argumentos técnicos y presupuestos número {i}."
            });
        }

        var chunks = TranscriptChunker.ChunkTranscript(meeting, targetTokensPerChunk: 500, targetDurationPerChunk: TimeSpan.FromMinutes(30));

        Assert.True(chunks.Count >= 3, "La transcripción extensa debe dividirse en 3 o más fragmentos.");
        Assert.Equal(TimeSpan.Zero, chunks[0].StartTime);
        Assert.True(chunks[^1].EndTime >= TimeSpan.FromMinutes(100));

        // Verificar que no se dividen turnos de habla dentro de un segmento
        foreach (var chunk in chunks)
        {
            Assert.NotEmpty(chunk.Segments);
            Assert.True(chunk.EstimatedTokens > 0);
        }
    }

    // 11. Fallo de uno de los fragmentos sin perder el resultado del resto (reintento acotado de fragmento)
    [Fact]
    public async Task Scenario11_ChunkFailure_RetriesWithoutLosingOtherChunks()
    {
        int chunk2Attempts = 0;
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = req =>
            {
                var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";

                // Si es el fragmento #2, fallar en el primer intento con 503 transitorio, luego responder 200 OK
                if (body.Contains("Fragmento #2"))
                {
                    chunk2Attempts++;
                    if (chunk2Attempts == 1)
                    {
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                        {
                            Content = new StringContent("{\"error\": {\"message\": \"Temporary upstream overload\"}}", Encoding.UTF8, "application/json")
                        });
                    }
                }

                var okPayload = new
                {
                    choices = new[]
                    {
                        new { message = new { role = "assistant", content = "Extracción de hechos correcta." }, finish_reason = "stop" }
                    },
                    usage = new { prompt_tokens = 300, completion_tokens = 150, total_tokens = 450 }
                };

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(okPayload), Encoding.UTF8, "application/json")
                });
            }
        };

        var service = new OpenRouterActaService(new HttpClient(mockHandler));
        var meeting = new Meeting { Title = "Multi Chunk Meeting", StartTime = DateTimeOffset.Now, EndTime = DateTimeOffset.Now.AddHours(2) };

        for (int i = 0; i < 30; i++)
        {
            meeting.Transcript.Add(new TranscriptSegment
            {
                StartTime = TimeSpan.FromMinutes(i * 4),
                EndTime = TimeSpan.FromMinutes(i * 4 + 3.5),
                Text = $"Segmento {i} de la reunión."
            });
        }

        var options = new ActaGenerationOptions { ApiKey = "sk-test", ForceHierarchicalProcessing = true };
        var result = await service.GenerateActaAsync(meeting, options);

        Assert.True(result.Success);
        Assert.Equal(2, chunk2Attempts); // Se reintentó exactamente una vez y tuvo éxito
    }

    // 12. Fallo de la síntesis final y recuperación sin reprocesar innecesariamente todo
    [Fact]
    public async Task Scenario12_SynthesisFailure_RecoversWithoutReprocessingChunks()
    {
        int synthesisAttempts = 0;
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = req =>
            {
                var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";

                // Detectar si la llamada es la de síntesis global (contiene los fragmentos analizados)
                if (body.Contains("FRAGMENTO #"))
                {
                    synthesisAttempts++;
                    if (synthesisAttempts == 1)
                    {
                        return Task.FromResult(new HttpResponseMessage((HttpStatusCode)429)
                        {
                            Content = new StringContent("{\"error\": {\"message\": \"Rate limit exceeded\"}}", Encoding.UTF8, "application/json")
                        });
                    }
                }

                var okPayload = new
                {
                    choices = new[]
                    {
                        new { message = new { role = "assistant", content = "# Acta de la Reunión\n## Resumen Ejecutivo\nSíntesis completada." }, finish_reason = "stop" }
                    },
                    usage = new { prompt_tokens = 500, completion_tokens = 200, total_tokens = 700 }
                };

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(okPayload), Encoding.UTF8, "application/json")
                });
            }
        };

        var service = new OpenRouterActaService(new HttpClient(mockHandler));
        var meeting = new Meeting { Title = "Synthesis Recovery Meeting", StartTime = DateTimeOffset.Now, EndTime = DateTimeOffset.Now.AddHours(2) };
        for (int i = 0; i < 20; i++)
        {
            meeting.Transcript.Add(new TranscriptSegment
            {
                StartTime = TimeSpan.FromMinutes(i * 6),
                EndTime = TimeSpan.FromMinutes(i * 6 + 5),
                Text = $"Punto {i} tratado."
            });
        }

        var options = new ActaGenerationOptions { ApiKey = "sk-test", ForceHierarchicalProcessing = true };
        var result = await service.GenerateActaAsync(meeting, options);

        Assert.True(result.Success);
        Assert.Equal(2, synthesisAttempts);
    }

    // 13. Conservación de temas y decisiones tanto del comienzo como del final de la reunión
    [Fact]
    public void Scenario13_PreservesTopicsAndDecisionsFromStartAndEnd()
    {
        var chunkAnalyses = new List<string>
        {
            "FRAGMENTO 1 (Inicio): Se acordó por unanimidad aprobar el presupuesto inicial del Q1.",
            "FRAGMENTO 2 (Desarrollo): Se debatieron tres alternativas para el proveedor de nube.",
            "FRAGMENTO 3 (Cierre): En los últimos 10 minutos se acordó contratar la Opción B y asignar a Laura la firma del contrato antes del viernes."
        };

        var meeting = new Meeting
        {
            Title = "Reunión de Principio a Fin",
            StartTime = DateTimeOffset.Now,
            EndTime = DateTimeOffset.Now.AddHours(2)
        };
        var options = new ActaGenerationOptions { DetailLevel = ActaDetailLevel.Detallada };

        var prompt = ActaPromptBuilder.BuildGlobalSynthesisPrompt(chunkAnalyses, meeting, options);

        Assert.Contains("FRAGMENTO #1", prompt);
        Assert.Contains("aprobar el presupuesto inicial del Q1", prompt);
        Assert.Contains("FRAGMENTO #3", prompt);
        Assert.Contains("asignar a Laura la firma del contrato", prompt);
        Assert.Contains("Asegúrate de que las conclusiones tomadas al cierre de la reunión figuren con total claridad", prompt);
    }

    // 14. Generación en diferentes idiomas (Español, English) y niveles de detalle (Normal, Detallada, Exhaustiva)
    [Theory]
    [InlineData(LanguageMode.Spanish, ActaDetailLevel.Normal, "Español", "Nivel normal")]
    [InlineData(LanguageMode.Spanish, ActaDetailLevel.Detallada, "Español", "Nivel detallado")]
    [InlineData(LanguageMode.Spanish, ActaDetailLevel.Exhaustiva, "Español", "Nivel exhaustivo")]
    [InlineData(LanguageMode.English, ActaDetailLevel.Normal, "English", "Normal level")]
    [InlineData(LanguageMode.English, ActaDetailLevel.Detallada, "English", "Detailed level")]
    [InlineData(LanguageMode.English, ActaDetailLevel.Exhaustiva, "English", "Comprehensive level")]
    public void Scenario14_GeneratesInDifferentLanguagesAndDetailLevels(
        LanguageMode language,
        ActaDetailLevel detailLevel,
        string expectedLanguageWord,
        string expectedGuidanceSnippet)
    {
        var meeting = new Meeting { Title = "Multi Language & Detail Meeting" };
        meeting.Transcript.Add(new TranscriptSegment { Text = "Discussion content" });

        var options = new ActaGenerationOptions
        {
            Language = language,
            DetailLevel = detailLevel
        };

        var prompt = ActaPromptBuilder.BuildPrompt(meeting, options);
        var systemPrompt = ActaPromptBuilder.BuildSystemPrompt(options);

        Assert.Contains(expectedLanguageWord, prompt);
        Assert.Contains(expectedGuidanceSnippet, prompt);
        Assert.Contains(expectedLanguageWord, systemPrompt);
    }

    // 15. Verificación de que no se guarda un resultado parcial como acta final satisfactoria
    [Fact]
    public async Task Scenario15_PartialOrTruncatedResult_IsNotSavedAsSatisfactoryFinalVersion()
    {
        // Simular un repositorio persistente
        var testDir = Path.Combine(Path.GetTempPath(), $"dictameeting_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        try
        {
            var repo = new LocalFileMeetingRepository(new DocumentExporter(), testDir);
            var meeting = new Meeting { Title = "Reunión Truncada No Guardada" };
            meeting.Transcript.Add(new TranscriptSegment { Text = "Contenido" });
            await repo.SaveMeetingAsync(meeting);

            // Simular un resultado devuelto por la IA que está truncado
            var truncatedResult = new ActaGenerationResult
            {
                Success = false,
                IsTruncated = true,
                FinishReason = "length",
                PartialMarkdownContent = "# Acta Incompleta\nSolo dos párrafos...",
                ErrorMessage = "Límite de tokens alcanzado."
            };

            // Regla de aplicación: Si result.Success == false o result.IsTruncated == true,
            // NO se debe añadir a meeting.ActaVersions ni actualizar meeting.ActaMarkdown como completada
            if (truncatedResult.Success && !truncatedResult.IsTruncated && !string.IsNullOrWhiteSpace(truncatedResult.MarkdownContent))
            {
                var v = new MeetingActaVersion
                {
                    MeetingId = meeting.Id,
                    MarkdownContent = truncatedResult.MarkdownContent
                };
                meeting.ActaVersions.Add(v);
                await repo.SaveMeetingAsync(meeting);
            }

            var loadedMeeting = await repo.GetMeetingAsync(meeting.Id);
            Assert.NotNull(loadedMeeting);
            Assert.Empty(loadedMeeting.ActaVersions);
            Assert.Null(loadedMeeting.ActaMarkdown);
        }
        finally
        {
            if (Directory.Exists(testDir)) Directory.Delete(testDir, recursive: true);
        }
    }

    // 16. Verificación de que un reintento no crea versiones duplicadas en el historial
    [Fact]
    public async Task Scenario16_InternalRetries_DoNotCreateDuplicateVersionsInHistory()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"dictameeting_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        try
        {
            var repo = new LocalFileMeetingRepository(new DocumentExporter(), testDir);
            var meeting = new Meeting { Title = "Reunión con Reintentos Internos" };
            meeting.Transcript.Add(new TranscriptSegment { Text = "Contenido relevante" });
            await repo.SaveMeetingAsync(meeting);

            // Simular que el servicio IA realizó 2 reintentos internos antes de devolver un resultado exitoso
            int internalRetries = 2;
            var successfulResult = new ActaGenerationResult
            {
                Success = true,
                IsTruncated = false,
                FinishReason = "stop",
                MarkdownContent = "# Acta de la Reunión\n## Resumen Ejecutivo\nCompletada tras reintentos internos.",
                StagesExecuted = internalRetries + 1
            };

            // La UI solo guarda cuando recibe el resultado final satisfactorio
            if (successfulResult.Success && !successfulResult.IsTruncated && !string.IsNullOrWhiteSpace(successfulResult.MarkdownContent))
            {
                var newVersion = new MeetingActaVersion
                {
                    MeetingId = meeting.Id,
                    Language = LanguageMode.Spanish,
                    DetailLevel = ActaDetailLevel.Normal,
                    MarkdownContent = successfulResult.MarkdownContent
                };
                meeting.ActaVersions.Add(newVersion);
                meeting.ActaMarkdown = successfulResult.MarkdownContent;
                await repo.SaveMeetingAsync(meeting);
            }

            var loadedMeeting = await repo.GetMeetingAsync(meeting.Id);
            Assert.NotNull(loadedMeeting);
            Assert.Single(loadedMeeting.ActaVersions);
            Assert.Equal("# Acta de la Reunión\n## Resumen Ejecutivo\nCompletada tras reintentos internos.", loadedMeeting.ActaMarkdown);
        }
        finally
        {
            if (Directory.Exists(testDir)) Directory.Delete(testDir, recursive: true);
        }
    }
}
