using System.Globalization;
using System.Text;
using MarkItDownSharp.Converters.Internal;
using MarkItDownSharp.Core;
using MsgReader.Outlook;

namespace MarkItDownSharp.Converters;

public sealed class OutlookMessageConverter : IMarkItDownConverter, IExtensionProvider
{
    public int Priority => 0;
    public string Name => "Outlook message (MSG)";
    public string Description => "Outlook .msg e-mails: headers, body and a list of attachments.";
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".msg" };

    public bool CanConvert(StreamMetadata metadata) =>
        Extensions.Contains(metadata.Extension, StringComparer.OrdinalIgnoreCase);

    public Task<ConversionResult> ConvertAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken = default)
    {
        using var message = new Storage.Message(stream, FileAccess.Read);
        var output = new StringBuilder();
        var subject = string.IsNullOrWhiteSpace(message.Subject) ? Path.GetFileNameWithoutExtension(metadata.FileName) : message.Subject.Trim();
        output.Append("# ").Append(subject).Append("\n\n");

        AppendHeader(output, "From", FormatAddress(message.Sender?.DisplayName, message.Sender?.Email));
        AppendHeader(output, "To", FormatRecipients(message, RecipientType.To));
        AppendHeader(output, "Cc", FormatRecipients(message, RecipientType.Cc));
        AppendHeader(output, "Date", message.SentOn?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        output.Append('\n');

        var body = !string.IsNullOrWhiteSpace(message.BodyHtml)
            ? HtmlConverter.ConvertHtml(message.BodyHtml).Markdown
            : TextDecoding.NormalizeNewlines(message.BodyText ?? string.Empty).Trim();
        output.Append(body).Append("\n\n");

        var attachmentNames = message.Attachments
            .OfType<Storage.Attachment>()
            .Select(a => a.FileName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();
        if (attachmentNames.Count > 0)
        {
            output.Append("## Attachments\n\n");
            foreach (var attachment in attachmentNames)
            {
                output.Append("- ").Append(attachment).Append('\n');
            }
        }

        return Task.FromResult(new ConversionResult(output.ToString().Trim(), subject));
    }

    private static void AppendHeader(StringBuilder output, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            output.Append("**").Append(name).Append(":** ").Append(value).Append("  \n");
        }
    }

    private static string? FormatRecipients(Storage.Message message, RecipientType type)
    {
        var recipients = message.Recipients
            .Where(r => r.Type == type)
            .Select(r => FormatAddress(r.DisplayName, r.Email))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        return recipients.Count == 0 ? null : string.Join(", ", recipients);
    }

    private static string? FormatAddress(string? name, string? email) =>
        (string.IsNullOrWhiteSpace(name), string.IsNullOrWhiteSpace(email)) switch
        {
            (false, false) when !string.Equals(name, email, StringComparison.OrdinalIgnoreCase) => $"{name} <{email}>",
            (false, _) => name,
            (_, false) => email,
            _ => null
        };
}
