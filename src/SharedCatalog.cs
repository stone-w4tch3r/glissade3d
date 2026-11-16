using System;
using System.Collections.Generic;
using System.Globalization;

namespace Glissade3D;

public sealed class SharedEntry
{
    public string Id = "";
    public string Name = "";
    public string File = "";
    public string Sha256 = "";
}

/// <summary>
/// Static index of the routes published in the shared folder. The manifest is
/// fetched over HTTP; every file is verified against its SHA-256 before use.
/// </summary>
public sealed class SharedCatalog
{
    public const int FormatVersion = 1;

    public int Version;
    public readonly List<SharedEntry> Entries = new List<SharedEntry>();

    public static SharedCatalog Parse(string text, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "empty manifest";
            return null;
        }

        ConfigNode root;
        try
        {
            root = ConfigNode.Parse(text.TrimStart('\uFEFF'));
        }
        catch (Exception e)
        {
            error = "manifest parse: " + e.Message;
            return null;
        }

        if (root == null)
        {
            error = "manifest parse failed";
            return null;
        }

        var node = root.name == "SharedRoutes" ? root : root.GetNode("SharedRoutes");
        if (node == null && root.GetNodes("Entry").Length > 0) node = root;
        if (node == null)
        {
            error = "manifest has no SharedRoutes node";
            return null;
        }

        var catalog = new SharedCatalog();
        if (!int.TryParse(node.GetValue("version"), NumberStyles.Integer, CultureInfo.InvariantCulture, out catalog.Version))
            catalog.Version = 0;
        if (catalog.Version != FormatVersion)
        {
            error = $"unsupported manifest version {catalog.Version}";
            return null;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entryNode in node.GetNodes("Entry"))
        {
            var entry = new SharedEntry
            {
                Id = (entryNode.GetValue("id") ?? "").Trim(),
                Name = (entryNode.GetValue("name") ?? "").Trim(),
                File = (entryNode.GetValue("file") ?? "").Trim().Replace('\\', '/'),
                Sha256 = (entryNode.GetValue("sha256") ?? "").Trim().ToLowerInvariant()
            };

            if (entry.Id.Length == 0 || entry.Name.Length == 0 || entry.File.Length == 0 || entry.Sha256.Length != 64)
            {
                error = "manifest entry is incomplete";
                return null;
            }

            if (!IsHex(entry.Sha256))
            {
                error = "manifest entry has a bad hash";
                return null;
            }

            if (!IsSafeRelative(entry.File))
            {
                error = "unsafe manifest path: " + entry.File;
                return null;
            }

            if (!seen.Add(entry.Id))
            {
                error = "duplicate id in manifest: " + entry.Id;
                return null;
            }

            catalog.Entries.Add(entry);
        }

        if (catalog.Entries.Count == 0)
        {
            error = "manifest has no entries";
            return null;
        }

        return catalog;
    }

    private static bool IsHex(string value)
    {
        foreach (var ch in value)
        {
            var digit = (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f');
            if (!digit) return false;
        }
        return true;
    }

    private static bool IsSafeRelative(string path)
    {
        if (path.Length == 0 || path.StartsWith("/", StringComparison.Ordinal) || path.Contains(":")) return false;
        if (!path.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(path, "index.cfg", StringComparison.OrdinalIgnoreCase)) return false;

        foreach (var part in path.Split('/'))
        {
            if (part.Length == 0 || part == "." || part == "..") return false;
        }
        return true;
    }
}
