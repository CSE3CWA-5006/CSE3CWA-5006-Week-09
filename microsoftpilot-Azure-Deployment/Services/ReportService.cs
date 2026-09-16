using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MicrosoftPilot.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MicrosoftPilot.Services;

// ReportService converts DashboardReport into DOCX or PDF. The app only reads
// Graph data; report generation happens locally on the web server.
public sealed class ReportService
{
    public byte[] CreateDocx(DashboardReport report)
    {
        using var stream = new MemoryStream();

        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(new Body());
            var body = mainPart.Document.Body!;

            body.Append(CreateHeading("Microsoft Account Daily Report", 32));
            body.Append(CreateParagraph($"Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm}"));
            body.Append(CreateParagraph($"Unread mail: {report.UnreadCount}"));
            body.Append(CreateParagraph($"Urgent mail: {report.HighImportanceCount}"));
            body.Append(CreateParagraph($"Mid importance mail: {report.NormalImportanceCount}"));
            body.Append(CreateParagraph($"Scheduled meetings: {report.Meetings.Count}"));
            body.Append(CreateParagraph($"Recent Word documents: {report.WordDocuments.Count}"));

            AddErrors(body, report);
            AddCommunitySection(body, report.Community);
            AddMailSection(body, report.Messages);
            AddMeetingsSection(body, report.Meetings);
            AddWordDocumentsSection(body, report.WordDocuments);

            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    public byte[] CreatePdf(DashboardReport report)
    {
        var document = QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(36);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(text => text.FontSize(10).FontFamily("Segoe UI"));

                page.Header()
                    .Text("Microsoft Account Daily Report")
                    .FontSize(22)
                    .SemiBold()
                    .FontColor(Colors.Blue.Medium);

                page.Content().Column(column =>
                {
                    column.Spacing(10);
                    column.Item().Text($"Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm}");
                    column.Item().Text($"Unread mail: {report.UnreadCount}    Urgent mail: {report.HighImportanceCount}    Mid mail: {report.NormalImportanceCount}    Meetings: {report.Meetings.Count}    Word docs: {report.WordDocuments.Count}");

                    if (report.Errors.Count > 0)
                    {
                        column.Item().Text("Warnings").FontSize(14).SemiBold();
                        foreach (var error in report.Errors)
                        {
                            column.Item().Text(error).FontColor(Colors.Red.Darken2);
                        }
                    }

                    if (report.Community is not null)
                    {
                        column.Item().Text("Account Profile").FontSize(14).SemiBold();
                        column.Item().Text($"{report.Community.DisplayName} | {report.Community.Mail} | {report.Community.UserPrincipalName}");
                    }

                    AddPdfList(column, "Today's Outlook Mail", report.Messages.Select(message =>
                        $"{message.Subject} | {message.From} | {message.ReceivedAt:HH:mm} | {message.Importance} | {(message.IsRead ? "Read" : "Unread")}"));

                    AddPdfList(column, "Scheduled Meetings", report.Meetings.Select(meeting =>
                        $"{meeting.Subject} | {meeting.Start:yyyy-MM-dd HH:mm} | {meeting.Organizer} | {meeting.Location}"));

                    AddPdfList(column, "Latest Modified Word Documents", report.WordDocuments.Select(doc =>
                        $"{doc.Name} | {doc.LastModifiedAt:yyyy-MM-dd HH:mm} | {FormatBytes(doc.Size)}"));
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        });

        return document.GeneratePdf();
    }

    private static void AddPdfList(ColumnDescriptor column, string title, IEnumerable<string> rows)
    {
        column.Item().Text(title).FontSize(14).SemiBold();
        var any = false;

        foreach (var row in rows)
        {
            any = true;
            column.Item().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingBottom(4).Text(row);
        }

        if (!any)
        {
            column.Item().Text("No data returned.").FontColor(Colors.Grey.Darken1);
        }
    }

    private static void AddErrors(Body body, DashboardReport report)
    {
        if (report.Errors.Count == 0)
        {
            return;
        }

        body.Append(CreateHeading("Warnings", 24));
        foreach (var error in report.Errors)
        {
            body.Append(CreateParagraph(error));
        }
    }

    private static void AddCommunitySection(Body body, CommunitySummary? community)
    {
        body.Append(CreateHeading("Account Profile", 24));

        if (community is null)
        {
            body.Append(CreateParagraph("No account profile was returned."));
            return;
        }

        body.Append(CreateParagraph($"Display name: {community.DisplayName}"));
        body.Append(CreateParagraph($"Mail: {community.Mail}"));
        body.Append(CreateParagraph($"User principal name: {community.UserPrincipalName}"));
        body.Append(CreateParagraph($"Preferred language: {community.PreferredLanguage}"));
    }

    private static void AddMailSection(Body body, IReadOnlyList<MailSummary> messages)
    {
        body.Append(CreateHeading("Today's Outlook Mail", 24));

        if (messages.Count == 0)
        {
            body.Append(CreateParagraph("No messages were returned for today."));
            return;
        }

        foreach (var message in messages)
        {
            body.Append(CreateParagraph(message.Subject, bold: true));
            body.Append(CreateParagraph($"{message.From} | {message.ReceivedAt:HH:mm} | {message.Importance} | {(message.IsRead ? "Read" : "Unread")}"));
        }
    }

    private static void AddMeetingsSection(Body body, IReadOnlyList<MeetingSummary> meetings)
    {
        body.Append(CreateHeading("Scheduled Meetings", 24));

        if (meetings.Count == 0)
        {
            body.Append(CreateParagraph("No scheduled meetings were returned."));
            return;
        }

        foreach (var meeting in meetings)
        {
            body.Append(CreateParagraph(meeting.Subject, bold: true));
            body.Append(CreateParagraph($"{meeting.Start:yyyy-MM-dd HH:mm} - {meeting.End:HH:mm} | {meeting.Organizer} | {meeting.Location}"));
        }
    }

    private static void AddWordDocumentsSection(Body body, IReadOnlyList<WordDocumentSummary> documents)
    {
        body.Append(CreateHeading("Latest Modified Word Documents", 24));

        if (documents.Count == 0)
        {
            body.Append(CreateParagraph("No .docx files were returned from OneDrive."));
            return;
        }

        foreach (var document in documents)
        {
            body.Append(CreateParagraph(document.Name, bold: true));
            body.Append(CreateParagraph($"{document.LastModifiedAt:yyyy-MM-dd HH:mm} | {FormatBytes(document.Size)}"));
        }
    }

    private static Paragraph CreateHeading(string text, int fontSize)
    {
        return new Paragraph(
            new Run(
                new RunProperties(
                    new Bold(),
                    new FontSize { Val = fontSize.ToString() },
                    new DocumentFormat.OpenXml.Wordprocessing.Color { Val = "2563EB" }),
                new Text(text)));
    }

    private static Paragraph CreateParagraph(string text, bool bold = false)
    {
        var runProperties = bold ? new RunProperties(new Bold()) : new RunProperties();

        return new Paragraph(new Run(runProperties, new Text(text)
        {
            Space = SpaceProcessingModeValues.Preserve
        }));
    }

    private static string FormatBytes(long? bytes)
    {
        if (bytes is null)
        {
            return "Unknown size";
        }

        return bytes.Value >= 1024 * 1024
            ? $"{bytes.Value / 1024d / 1024d:0.0} MB"
            : $"{bytes.Value / 1024d:0.0} KB";
    }
}
