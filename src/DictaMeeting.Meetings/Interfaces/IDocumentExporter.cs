using DictaMeeting.Meetings.Models;

namespace DictaMeeting.Meetings.Interfaces;

public interface IDocumentExporter
{
    string ExportToMarkdown(Meeting meeting);
    string ExportToPlainText(Meeting meeting);
    string ExportSummaryToPlainText(Meeting meeting);
    string ExportToJson(Meeting meeting);
}
