using System;
using System.Collections.Generic;
using System.Text;

namespace Glissade3D;

/// <summary>
/// File names for one-route-per-file storage. The file name is cosmetic: the
/// route id inside the file is the identity. The slug keeps Unicode letters, so
/// Cyrillic and CJK names stay readable on disk.
/// </summary>
public static class RouteNaming
{
    public const int MaxStem = 64;

    private static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "con", "prn", "aux", "nul",
        "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9"
    };

    public static string Slug(string name, string id)
    {
        var builder = new StringBuilder();
        var lastWasDash = false;

        foreach (var raw in (name ?? "").Trim())
        {
            var ch = char.ToLowerInvariant(raw);
            if (char.IsLetterOrDigit(ch) || ch == '_')
            {
                builder.Append(ch);
                lastWasDash = false;
                continue;
            }

            if (ch == '-' && (builder.Length == 0 || lastWasDash)) continue;
            if (builder.Length == 0 || lastWasDash) continue;
            builder.Append('-');
            lastWasDash = true;
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > MaxStem) slug = slug.Substring(0, MaxStem).TrimEnd('-');
        if (slug.Length == 0) slug = "route-" + ShortId(id);
        if (Reserved.Contains(slug)) slug = "_" + slug;
        return slug;
    }

    /// <summary>Appends -2, -3 ... until the stem is free (case-insensitive).</summary>
    public static string UniqueStem(string slug, string id, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (taken != null)
        {
            foreach (var stem in taken)
            {
                if (!string.IsNullOrEmpty(stem)) used.Add(stem);
            }
        }

        if (!used.Contains(slug)) return slug;

        for (var n = 2; n < 1000; n++)
        {
            var candidate = slug + "-" + n;
            if (!used.Contains(candidate)) return candidate;
        }

        return slug + "-" + ShortId(id);
    }

    private static string ShortId(string id)
    {
        if (string.IsNullOrEmpty(id)) return "new";
        return id.Length <= 6 ? id : id.Substring(id.Length - 6);
    }
}
