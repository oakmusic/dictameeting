using System.IO;
using DictaMeeting.Infrastructure.Persistence;
using DictaMeeting.Meetings.Interfaces;
using Xunit;

namespace DictaMeeting.Tests;

public class MeetingDirectoryResolutionTests
{
    [Fact]
    public void ResolveDefaultMeetingsDirectory_WhenInsideDataFolder_ResolvesToSiblingMeetingsDirectory()
    {
        // Simular ejecución desde C:\Apps\DictaMeeting\data\ o C:\Apps\DictaMeeting\data
        var dataFolder = Path.Combine("C:", "Apps", "DictaMeeting", "data");
        var resolved = LocalFileMeetingRepository.ResolveDefaultMeetingsDirectory(dataFolder);

        var expected = Path.Combine("C:", "Apps", "DictaMeeting", "Meetings");
        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void ResolveDefaultMeetingsDirectory_WhenInsideDataFolderWithTrailingSlash_ResolvesToSiblingMeetingsDirectory()
    {
        var dataFolderWithSlash = Path.Combine("C:", "Apps", "DictaMeeting", "data") + Path.DirectorySeparatorChar;
        var resolved = LocalFileMeetingRepository.ResolveDefaultMeetingsDirectory(dataFolderWithSlash);

        var expected = Path.Combine("C:", "Apps", "DictaMeeting", "Meetings");
        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void ResolveDefaultMeetingsDirectory_WhenInNormalFolder_ResolvesToSubMeetingsDirectory()
    {
        var normalFolder = Path.Combine("C:", "Apps", "DictaMeeting");
        var resolved = LocalFileMeetingRepository.ResolveDefaultMeetingsDirectory(normalFolder);

        var expected = Path.Combine("C:", "Apps", "DictaMeeting", "Meetings");
        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void Constructor_WhenExplicitBaseDirectoryProvided_UsesExplicitDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DictaMeeting_CustomMeetings_" + Path.GetRandomFileName());
        try
        {
            var exporter = new DictaMeeting.Meetings.Exporters.DocumentExporter();
            var repo = new LocalFileMeetingRepository(exporter, tempDir);

            Assert.Equal(tempDir, repo.BaseDirectory);
            Assert.True(Directory.Exists(tempDir));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
