namespace MarkItDownSharp.Core;

public interface IExtensionProvider
{
    IReadOnlyCollection<string> Extensions { get; }
}
