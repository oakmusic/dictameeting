using DictaMeeting.Meetings.Enums;
using DictaMeeting.Transcription.Models;
using DictaMeeting.Transcription.Services;
using Xunit;

namespace DictaMeeting.Tests;

public class TranscriptionServiceTests
{
    [Fact]
    public async Task InitializeAsync_SetsModelAndInitializesService()
    {
        // Arrange
        using var service = new MockTranscriptionService();

        // Act
        await service.InitializeAsync(ModelSize.Small);

        // Assert
        Assert.True(service.IsInitialized);
        Assert.Equal(ModelSize.Small, service.CurrentModel);
    }

    [Fact]
    public async Task TranscribeAudioChunkAsync_WithPcmData_ReturnsSegmentWithText()
    {
        // Arrange
        using var service = new MockTranscriptionService();
        byte[] dummyPcm = new byte[3200]; // 100 ms de PCM 16-bit a 16 kHz

        // Act
        var segment = await service.TranscribeAudioChunkAsync(dummyPcm, TimeSpan.FromSeconds(10));

        // Assert
        Assert.NotNull(segment);
        Assert.Equal(TimeSpan.FromSeconds(10), segment.StartTime);
        Assert.False(string.IsNullOrWhiteSpace(segment.Text));
    }

    [Fact]
    public void LanguageSelection_CanBeToggled()
    {
        // Arrange
        using var service = new MockTranscriptionService();

        // Act & Assert
        service.CurrentLanguage = LanguageMode.English;
        Assert.Equal(LanguageMode.English, service.CurrentLanguage);

        service.CurrentLanguage = LanguageMode.Auto;
        Assert.Equal(LanguageMode.Auto, service.CurrentLanguage);
    }
}
