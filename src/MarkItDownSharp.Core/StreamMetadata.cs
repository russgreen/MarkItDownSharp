namespace MarkItDownSharp.Core;

public sealed record StreamMetadata(
    string? FileName = null,
    string? Extension = null,
    string? MimeType = null,
    string? Charset = null,
    string? LocalPath = null);
