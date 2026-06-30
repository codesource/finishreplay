using System.Text;

namespace FinishReplay.Services.Naming;

/// <summary>Context values substituted into a clip filename template.</summary>
public sealed record RecordingNameContext(
    DateTimeOffset Date,
    string Category,
    string Discipline,
    int Series,
    string CameraSuffix);

/// <summary>
/// Builds clip filenames from a template such as
/// <c>{date}-{category}-{discipline}-{serie}-{camera}</c>. Tokens are case-insensitive; missing
/// values collapse cleanly so you never get stray separators. The result is filename-safe and has
/// no extension (callers add e.g. <c>.mp4</c>).
/// </summary>
public static class FilenameFormatter
{
    public static string Build(string template, RecordingNameContext ctx)
    {
        if (string.IsNullOrWhiteSpace(template))
            template = "{date}-{camera}";

        var result = template
            .ReplaceToken("date", ctx.Date.ToString("yyyyMMdd"))
            .ReplaceToken("category", ctx.Category)
            .ReplaceToken("discipline", ctx.Discipline)
            // Accept {serie}, {series} and the literal {#serie} the user often writes.
            .ReplaceToken("#serie", ctx.Series.ToString())
            .ReplaceToken("series", ctx.Series.ToString())
            .ReplaceToken("serie", ctx.Series.ToString())
            .ReplaceToken("camera", ctx.CameraSuffix)
            .ReplaceToken("suffix", ctx.CameraSuffix);

        return Tidy(result);
    }

    private static string ReplaceToken(this string s, string token, string value) =>
        s.Replace("{" + token + "}", value ?? "", StringComparison.OrdinalIgnoreCase);

    /// <summary>Strip invalid characters and collapse separators left by empty tokens.</summary>
    private static string Tidy(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0)
                continue;
            sb.Append(c);
        }

        var cleaned = sb.ToString();

        // Collapse runs of separators produced by empty values, then trim them from the ends.
        while (cleaned.Contains("--")) cleaned = cleaned.Replace("--", "-");
        while (cleaned.Contains("__")) cleaned = cleaned.Replace("__", "_");
        cleaned = cleaned.Trim('-', '_', ' ', '.');

        return cleaned.Length == 0 ? "clip" : cleaned;
    }
}
