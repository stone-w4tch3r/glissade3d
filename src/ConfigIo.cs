using System;
using System.IO;
using UnityEngine;

namespace Glissade3D;

/// <summary>
/// cfg loading that survives a UTF-8 byte order mark. Editors such as Notepad
/// add one, and ConfigNode.Load then reads the first key with the mark glued to
/// it, silently losing every setting in the file. File.ReadAllText strips it.
/// </summary>
public static class ConfigIo
{
    public static ConfigNode Load(string path)
    {
        if (!File.Exists(path)) return null;
        var text = File.ReadAllText(path);
        if (string.IsNullOrEmpty(text)) return null;
        var node = ConfigNode.Parse(text);
        Dbg.Line($"cfg {Path.GetFileName(path)}: bytes={new FileInfo(path).Length} chars={text.Length} " +
                 $"node={node?.name} children={(node != null ? node.GetNodes().Length : -1)} first={FirstChar(text)}");
        return node;
    }

    /// <summary>
    /// Writes through a temporary file so a crash never leaves half a file.
    /// File.Replace keeps the previous version as .bak; when it is unavailable
    /// the copy still overwrites the old file but never deletes it first.
    /// </summary>
    public static bool Save(ConfigNode node, string path, out string error)
    {
        return Save(node, path, true, out error);
    }

    /// <summary>Same write, optionally without a .bak next to the file.</summary>
    public static bool Save(ConfigNode node, string path, bool keepBackup, out string error)
    {
        error = "";
        if (node == null) return false;

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var backup = path + ".bak";
            try
            {
                node.Save(temp);

                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(temp, path, keepBackup ? backup : null);
                        return true;
                    }
                    catch (Exception e)
                    {
                        // Same folder, so this is unusual; fall back to a copy.
                        Dbg.Line($"replace failed for {Path.GetFileName(path)}: {e.Message}");
                    }

                    if (keepBackup) File.Copy(path, backup, true);
                }

                File.Copy(temp, path, true);
                return true;
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
        catch (Exception e)
        {
            error = e.Message;
            Debug.LogError($"[Glissade3D] save failed for {Path.GetFileName(path)}: {e.Message}");
            return false;
        }
    }

    private static string FirstChar(string text)
    {
        if (string.IsNullOrEmpty(text)) return "none";
        return ((int)text[0]).ToString();
    }
}
