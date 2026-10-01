using System;
using System.IO;
using System.Linq;
using DictaMeeting.Transcription.Services;
using Microsoft.ML.OnnxRuntime;
using Xunit;
using Xunit.Abstractions;

namespace DictaMeeting.Tests;

public class SileroModelInspectionTests
{
    private readonly ITestOutputHelper _output;

    public SileroModelInspectionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Inspect_SileroVadModels()
    {
        var streamingModel = SileroVoiceActivityDetector.ResolveModelPath(null, "silero_vad.onnx");
        var sequenceModel = SileroVoiceActivityDetector.ResolveModelPath(null, "silero_vad_16k_sequence.onnx");

        if (!File.Exists(streamingModel) || !File.Exists(sequenceModel))
        {
            _output.WriteLine("Modelos Silero VAD no encontrados en el entorno de pruebas. Prueba omitida limpiamente.");
            return;
        }

        using (var session = new InferenceSession(streamingModel))
        {
            _output.WriteLine("=== STREAMING MODEL (silero_vad.onnx) ===");
            foreach (var input in session.InputMetadata)
            {
                var dims = string.Join(",", input.Value.Dimensions);
                _output.WriteLine($"Input: {input.Key}, Type: {input.Value.ElementType}, Dims: [{dims}]");
            }
            foreach (var output in session.OutputMetadata)
            {
                var dims = string.Join(",", output.Value.Dimensions);
                _output.WriteLine($"Output: {output.Key}, Type: {output.Value.ElementType}, Dims: [{dims}]");
            }
        }

        using (var session = new InferenceSession(sequenceModel))
        {
            _output.WriteLine("=== SEQUENCE MODEL (silero_vad_16k_sequence.onnx) ===");
            foreach (var input in session.InputMetadata)
            {
                var dims = string.Join(",", input.Value.Dimensions);
                _output.WriteLine($"Input: {input.Key}, Type: {input.Value.ElementType}, Dims: [{dims}]");
            }
            foreach (var output in session.OutputMetadata)
            {
                var dims = string.Join(",", output.Value.Dimensions);
                _output.WriteLine($"Output: {output.Key}, Type: {output.Value.ElementType}, Dims: [{dims}]");
            }
        }
    }
}
