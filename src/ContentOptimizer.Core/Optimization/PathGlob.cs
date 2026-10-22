using System.Text;
using System.Text.RegularExpressions;

namespace ContentOptimizer.Core.Optimization;

public sealed class PathGlob
{
    private readonly Regex regex;

    public string Pattern { get; }

    public PathGlob(string pattern)
    {
        Pattern = pattern;
        regex = new Regex("^" + Translate(Normalize(pattern)) + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public bool IsMatch(string relativePath) => regex.IsMatch(Normalize(relativePath));

    private static string Normalize(string value) => value.Replace('\\', '/').TrimStart('/');

    private static string Translate(string pattern)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '*')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                {
                    if (i + 2 < pattern.Length && pattern[i + 2] == '/')
                    {
                        sb.Append("(?:.*/)?");
                        i += 2;
                    }
                    else
                    {
                        sb.Append(".*");
                        i += 1;
                    }
                }
                else
                {
                    sb.Append("[^/]*");
                }
            }
            else if (c == '?')
            {
                sb.Append("[^/]");
            }
            else
            {
                sb.Append(Regex.Escape(c.ToString()));
            }
        }

        return sb.ToString();
    }
}
