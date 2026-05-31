using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Parsing;

public interface IShowplanParser
{
    ShowplanDocument Parse(string xml);
}
