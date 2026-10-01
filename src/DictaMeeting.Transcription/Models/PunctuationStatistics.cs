using System;

namespace DictaMeeting.Transcription.Models;

/// <summary>
/// Métricas y estadísticas de telemetría del modelo de restauración de puntuación.
/// </summary>
public class PunctuationStatistics
{
    public string ModelName { get; set; } = "onnx-community/punctuate-all-ONNX (XLM-RoBERTa-base Int8)";
    public string Revision { get; set; } = "542ccb5c59daeb7f2fbd1d9fa2f14884bc103aa6";
    public string ModelPath { get; set; } = string.Empty;
    public long ModelSizeBytes { get; set; }
    public TimeSpan LoadDuration { get; set; }
    public TimeSpan TotalInferenceDuration { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public int TotalTokensProcessed { get; set; }
    public int TotalWordsProcessed { get; set; }
    public int TotalSegmentsProcessed { get; set; }
    public int TotalWindowsProcessed { get; set; }
}
