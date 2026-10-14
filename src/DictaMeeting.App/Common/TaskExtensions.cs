using System;
using System.Threading.Tasks;
using DictaMeeting.App.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DictaMeeting.App.Common;

/// <summary>
/// Extensiones para la ejecución segura de tareas asíncronas en segundo plano,
/// garantizando que todas las excepciones sean observadas, registradas y correlacionadas.
/// </summary>
public static class TaskExtensions
{
    /// <summary>
    /// Ejecuta una tarea asíncrona en modo fire-and-forget de forma segura,
    /// observando y registrando cualquier fallo para evitar que termine en el finalizador sin registrar.
    /// </summary>
    public static void SafeFireAndForget(
        this Task task,
        ILogger? logger = null,
        string? operationDescription = null)
    {
        if (task == null) return;

        task.ContinueWith(t =>
        {
            if (t.IsFaulted && t.Exception != null)
            {
                var flat = t.Exception.Flatten();
                var inner = flat.InnerException ?? flat;
                var op = operationDescription ?? "Tarea asíncrona en segundo plano";

                CrashDiagnosticTracker.AddBreadcrumb($"[Error en tarea de fondo] {op}: {inner.GetType().Name} - {inner.Message}");

                if (logger != null)
                {
                    logger.LogError(inner, "Excepción en {Operation}: {Message}", op, inner.Message);
                }
                else
                {
                    // Registro de respaldo si el logger es nulo
                    CrashDiagnosticTracker.RecordCrashReport(inner, $"TaskExtensions.SafeFireAndForget ({op})", isTerminating: false);
                }
            }
        }, TaskContinuationOptions.OnlyOnFaulted);
    }
}
