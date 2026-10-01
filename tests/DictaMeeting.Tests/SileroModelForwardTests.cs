using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DictaMeeting.Transcription.Services;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Xunit;

namespace DictaMeeting.Tests;

public class SileroModelForwardTests
{
    private static string? GetStreamingModelPath()
    {
        var path = SileroVoiceActivityDetector.ResolveModelPath(null, "silero_vad.onnx");
        return File.Exists(path) ? path : null;
    }

    private static string? GetSequenceModelPath()
    {
        var path = SileroVoiceActivityDetector.ResolveModelPath(null, "silero_vad_16k_sequence.onnx");
        return File.Exists(path) ? path : null;
    }

    [Fact]
    public void StreamingModel_ForwardPass_ReturnsProbability()
    {
        var modelPath = GetStreamingModelPath();
        if (modelPath == null) return;

        using var session = new InferenceSession(modelPath);

        // 576 samples: 64 context (zeros initially) + 512 audio samples
        float[] audioWithContext = new float[576];
        var inputTensor = new DenseTensor<float>(audioWithContext, new[] { 1, 576 });
        var stateTensor = new DenseTensor<float>(new float[2 * 1 * 128], new[] { 2, 1, 128 });
        var srTensor = new DenseTensor<long>(new long[] { 16000 }, Array.Empty<int>());

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", inputTensor),
            NamedOnnxValue.CreateFromTensor("state", stateTensor),
            NamedOnnxValue.CreateFromTensor("sr", srTensor)
        };

        using var results = session.Run(inputs);
        var probTensor = results.First(r => r.Name == "output").AsTensor<float>();
        var stateOut = results.First(r => r.Name == "stateN").AsTensor<float>();

        float prob = probTensor[0, 0];
        Assert.InRange(prob, 0f, 1f);
        Assert.Equal(2, stateOut.Dimensions[0]);
        Assert.Equal(1, stateOut.Dimensions[1]);
        Assert.Equal(128, stateOut.Dimensions[2]);
    }

    [Fact]
    public void SequenceModel_ForwardPass_ReturnsProbabilities()
    {
        var modelPath = GetSequenceModelPath();
        if (modelPath == null) return;

        using var session = new InferenceSession(modelPath);

        int frameCount = 10;
        float[] frames = new float[frameCount * 576];
        var inputTensor = new DenseTensor<float>(frames, new[] { frameCount, 576 });
        var hTensor = new DenseTensor<float>(new float[1 * 1 * 128], new[] { 1, 1, 128 });
        var cTensor = new DenseTensor<float>(new float[1 * 1 * 128], new[] { 1, 1, 128 });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", inputTensor),
            NamedOnnxValue.CreateFromTensor("h", hTensor),
            NamedOnnxValue.CreateFromTensor("c", cTensor)
        };

        using var results = session.Run(inputs);
        var probsTensor = results.First(r => r.Name == "speech_probs").AsTensor<float>();
        var hnOut = results.First(r => r.Name == "hn").AsTensor<float>();
        var cnOut = results.First(r => r.Name == "cn").AsTensor<float>();

        Assert.Equal(frameCount, probsTensor.Dimensions[0]);
        for (int i = 0; i < frameCount; i++)
        {
            Assert.InRange(probsTensor[i], 0f, 1f);
        }
    }
}
