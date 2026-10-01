using System.Collections.ObjectModel;
using DictaMeeting.App.ViewModels;
using DictaMeeting.Audio.Events;
using DictaMeeting.Audio.Interfaces;
using DictaMeeting.Audio.Models;
using DictaMeeting.Diarization.Services;
using DictaMeeting.Meetings.Models;
using Xunit;

namespace DictaMeeting.Tests;

public class ChannelAwareDiarizationAndParticipantTests
{
    [Fact]
    public void AudioChunkEventArgs_CarriesChannelAndRmsInformation()
    {
        byte[] pcm = new byte[3200];
        var args = new AudioChunkEventArgs(pcm, AudioDeviceType.Microphone, 0.05f, 0.001f);

        Assert.Equal(AudioDeviceType.Microphone, args.DominantSource);
        Assert.True(args.IsMicrophoneDominant);
        Assert.Equal(0.05f, args.MicRms);
        Assert.Equal(0.001f, args.SysRms);
    }

    [Fact]
    public void Meeting_ExpectedParticipants_CanBeAddedAndPersisted()
    {
        var meeting = new Meeting
        {
            Title = "Reunión de Estrategia"
        };
        meeting.ExpectedParticipants.Add("Aritz");
        meeting.ExpectedParticipants.Add("Laura");

        Assert.Equal(2, meeting.ExpectedParticipants.Count);
        Assert.Contains("Aritz", meeting.ExpectedParticipants);
        Assert.Contains("Laura", meeting.ExpectedParticipants);
    }

    [Fact]
    public void SpeakerViewModel_AvailableOptions_ReflectsRealParticipantsAndMaintainsSync()
    {
        var realParticipants = new ObservableCollection<string> { "Carlos", "Elena" };
        string? committedName = null;

        var speakerVm = new SpeakerViewModel(
            id: "SPEAKER_01",
            displayName: "SPEAKER_01",
            colorHex: "#3B82F6",
            onNameChanged: (_, _) => { },
            onNameCommitted: (_, name) => { committedName = name; },
            availableRealParticipants: realParticipants);

        // Debería tener la opción técnica ("SPEAKER_01") + "Carlos" + "Elena"
        Assert.Equal(3, speakerVm.AvailableOptions.Count);
        Assert.Contains("SPEAKER_01", speakerVm.AvailableOptions);
        Assert.Contains("Carlos", speakerVm.AvailableOptions);
        Assert.Contains("Elena", speakerVm.AvailableOptions);

        // Simular selección en ComboBox
        speakerVm.SelectedRealParticipant = "Elena";

        Assert.Equal("Elena", speakerVm.DisplayName);
        Assert.Equal("Elena", committedName);

        // Añadir nuevo participante a la lista compartida reactiva
        realParticipants.Add("David");
        Assert.Equal(4, speakerVm.AvailableOptions.Count);
        Assert.Contains("David", speakerVm.AvailableOptions);
    }

    [Fact]
    public void PyAnnoteCommunity1DiarizationService_Reset_ClearsKnownSpeakers()
    {
        var service = new PyAnnoteCommunity1DiarizationService();
        service.Reset();
        Assert.Empty(service.KnownSpeakers);
    }

    private static byte[] GenerateSyntheticVoicePcm(int durationMs, int baseFrequency)
    {
        int sampleRate = 16000;
        int totalSamples = sampleRate * durationMs / 1000;
        byte[] buffer = new byte[totalSamples * 2];

        for (int i = 0; i < totalSamples; i++)
        {
            double t = (double)i / sampleRate;
            double sample = 0.5 * Math.Sin(2.0 * Math.PI * baseFrequency * t)
                          + 0.3 * Math.Sin(2.0 * Math.PI * (baseFrequency * 2) * t)
                          + 0.2 * Math.Sin(2.0 * Math.PI * (baseFrequency * 3) * t);

            short s = (short)(sample * 16000);
            buffer[i * 2] = (byte)(s & 0xFF);
            buffer[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }

        return buffer;
    }
}
