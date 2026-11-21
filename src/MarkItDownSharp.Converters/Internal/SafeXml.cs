using System.Xml;
using System.Xml.Linq;

namespace MarkItDownSharp.Converters.Internal;

internal static class SafeXml
{
    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver = null,
        MaxCharactersFromEntities = 1024
    };

    public static XDocument Parse(string text, bool preserveWhitespace = false)
    {
        using var reader = XmlReader.Create(new StringReader(text), Settings);
        return XDocument.Load(reader, preserveWhitespace ? LoadOptions.PreserveWhitespace : LoadOptions.None);
    }

    public static XDocument Load(Stream stream)
    {
        using var reader = XmlReader.Create(stream, Settings);
        return XDocument.Load(reader, LoadOptions.None);
    }
}
