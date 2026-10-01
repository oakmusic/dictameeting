using System;
using System.IO;
using System.Threading.Tasks;
using DictaMeeting.Transcription.Models;
using DictaMeeting.Transcription.Services;
using NAudio.Wave;
using SherpaOnnx;
using Xunit;

namespace DictaMeeting.Tests;

public class Qwen3AsrTests
{
    [Fact]
    public void TestQwen3AsrConfigStructAssignment()
    {
        var qwenConfig = new OfflineQwen3AsrModelConfig
        {
            ConvFrontend = "frontend.onnx",
            Encoder = "encoder.onnx",
            Decoder = "decoder.onnx",
            Tokenizer = "tokenizer_dir",
            Hotwords = "prueba"
        };

        var modelConfig = new OfflineModelConfig
        {
            Qwen3Asr = qwenConfig,
            NumThreads = 4,
            Provider = "cpu"
        };

        var config = new OfflineRecognizerConfig
        {
            ModelConfig = modelConfig
        };

        Assert.Equal("frontend.onnx", config.ModelConfig.Qwen3Asr.ConvFrontend);
        Assert.Equal("encoder.onnx", config.ModelConfig.Qwen3Asr.Encoder);
        Assert.Equal("decoder.onnx", config.ModelConfig.Qwen3Asr.Decoder);
        Assert.Equal("tokenizer_dir", config.ModelConfig.Qwen3Asr.Tokenizer);
        Assert.Equal("prueba", config.ModelConfig.Qwen3Asr.Hotwords);
        Assert.Equal(4, config.ModelConfig.NumThreads);
    }

    [Fact]
    public async Task TestSherpaQwenTranscriptionIfModelAvailable()
    {
        string modelDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DictaMeeting", "models");
        string qwen06bPath = Path.Combine(modelDir, "qwen3-asr-0.6b-int8");
        if (!Directory.Exists(qwen06bPath)) return;

        var modelManager = new ModelManager(modelDir);
        using var qwenService = new SherpaQwenTranscriptionService(modelManager);

        await qwenService.InitializeAsync(ModelSize.Qwen3_06B);
        Assert.True(qwenService.IsInitialized);

        string wavPath = Path.Combine(qwen06bPath, "test_wavs", "es1.wav");
        if (File.Exists(wavPath))
        {
            using var waveReader = new WaveFileReader(wavPath);
            byte[] byteBuffer = new byte[waveReader.Length];
            int totalRead = 0;
            while (totalRead < byteBuffer.Length)
            {
                int read = waveReader.Read(byteBuffer, totalRead, byteBuffer.Length - totalRead);
                if (read <= 0) break;
                totalRead += read;
            }

            var chunkResult = await qwenService.TranscribeAudioChunkAsync(byteBuffer, TimeSpan.Zero);
            Assert.NotNull(chunkResult);
            Assert.False(string.IsNullOrWhiteSpace(chunkResult.Text));

            var fileSegments = await qwenService.TranscribeAudioFileAsync(wavPath);
            Assert.NotEmpty(fileSegments);
            Assert.False(string.IsNullOrWhiteSpace(fileSegments[0].Text));
        }
    }
}
