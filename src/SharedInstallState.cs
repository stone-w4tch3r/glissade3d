using System;
using System.Collections.Generic;
using UnityEngine;

namespace Glissade3D;

public sealed class SharedInstalledEntry
{
    public string Id = "";
    public string Name = "";
    public string Hash = "";
}

/// <summary>
/// Local record of routes installed from the shared catalog. It answers two
/// questions: which routes we manage, and whether the user has edited them since
/// (the canonical route hash no longer matches).
/// </summary>
public sealed class SharedInstallState
{
    public const int FormatVersion = 1;

    private readonly Dictionary<string, SharedInstalledEntry> _entries =
        new Dictionary<string, SharedInstalledEntry>(StringComparer.Ordinal);

    public IEnumerable<SharedInstalledEntry> Entries => _entries.Values;
    public int Count => _entries.Count;

    public bool Contains(string id) => !string.IsNullOrEmpty(id) && _entries.ContainsKey(id);

    public bool TryGet(string id, out SharedInstalledEntry entry)
    {
        return _entries.TryGetValue(id ?? "", out entry);
    }

    public void Set(string id, string name, string hash)
    {
        if (string.IsNullOrEmpty(id)) return;
        _entries[id] = new SharedInstalledEntry { Id = id, Name = name ?? "", Hash = hash ?? "" };
    }

    public bool Remove(string id) => !string.IsNullOrEmpty(id) && _entries.Remove(id);

    public static SharedInstallState Load(string path)
    {
        var state = new SharedInstallState();
        try
        {
            var file = ConfigIo.Load(path);
            if (file == null) return state;

            var node = file.name == "SharedInstalled" ? file : file.GetNode("SharedInstalled");
            if (node == null && file.GetNodes("Entry").Length > 0) node = file;
            if (node == null) return state;

            foreach (var entryNode in node.GetNodes("Entry"))
            {
                var entry = new SharedInstalledEntry
                {
                    Id = (entryNode.GetValue("id") ?? "").Trim(),
                    Name = (entryNode.GetValue("name") ?? "").Trim(),
                    Hash = (entryNode.GetValue("hash") ?? "").Trim()
                };
                if (entry.Id.Length > 0) state._entries[entry.Id] = entry;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Glissade3D] shared state load failed: " + e.Message);
        }
        return state;
    }

    public bool Save(string path, out string error)
    {
        var node = new ConfigNode("SharedInstalled");
        node.AddValue("version", FormatVersion.ToString());
        foreach (var entry in _entries.Values)
        {
            var entryNode = new ConfigNode("Entry");
            entryNode.AddValue("id", entry.Id);
            entryNode.AddValue("name", entry.Name);
            entryNode.AddValue("hash", entry.Hash);
            node.AddNode(entryNode);
        }
        return ConfigIo.Save(node, path, true, out error);
    }
}
