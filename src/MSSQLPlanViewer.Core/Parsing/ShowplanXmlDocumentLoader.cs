using System.Xml;
using System.Xml.Linq;

namespace MSSQLPlanViewer.Core.Parsing;

internal static class ShowplanXmlDocumentLoader
{
    public static XDocument Load(string xml, int maxXmlInputLength)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = maxXmlInputLength
        };

        using var stringReader = new StringReader(xml);
        using var xmlReader = XmlReader.Create(stringReader, settings);
        return XDocument.Load(xmlReader, LoadOptions.SetLineInfo);
    }
}
