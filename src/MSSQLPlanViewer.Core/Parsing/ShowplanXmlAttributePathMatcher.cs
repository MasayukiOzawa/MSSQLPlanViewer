namespace MSSQLPlanViewer.Core.Parsing;

internal static class ShowplanXmlAttributePathMatcher
{
    public static bool MatchesAny(string path, IEnumerable<string> patterns) =>
        patterns.Any(pattern => Matches(path, pattern));

    private static bool Matches(string path, string pattern)
    {
        var pathSegments = path.Split('.');
        var patternSegments = pattern.Split('.');
        return Matches(pathSegments, 0, patternSegments, 0);
    }

    private static bool Matches(
        IReadOnlyList<string> pathSegments,
        int pathIndex,
        IReadOnlyList<string> patternSegments,
        int patternIndex)
    {
        if (patternIndex >= patternSegments.Count)
        {
            return true;
        }

        if (pathIndex >= pathSegments.Count)
        {
            return patternSegments.Skip(patternIndex).All(segment => segment == "**");
        }

        var patternSegment = patternSegments[patternIndex];
        if (patternSegment == "**")
        {
            if (patternIndex == patternSegments.Count - 1)
            {
                return true;
            }

            for (var nextPathIndex = pathIndex; nextPathIndex <= pathSegments.Count; nextPathIndex++)
            {
                if (Matches(pathSegments, nextPathIndex, patternSegments, patternIndex + 1))
                {
                    return true;
                }
            }

            return false;
        }

        if (patternSegment != "*"
            && !NormalizeSegment(pathSegments[pathIndex]).StartsWith(patternSegment, StringComparison.Ordinal))
        {
            return false;
        }

        return Matches(pathSegments, pathIndex + 1, patternSegments, patternIndex + 1);
    }

    private static string NormalizeSegment(string segment)
    {
        var bracketIndex = segment.IndexOf('[');
        return bracketIndex >= 0
            ? segment[..bracketIndex]
            : segment;
    }
}
