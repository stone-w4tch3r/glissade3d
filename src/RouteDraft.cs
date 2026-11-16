using System.Collections.Generic;

namespace Glissade3D;

/// <summary>
/// An editing session for exactly one route. The editor mutates Draft only;
/// the stored route is unchanged until Apply. Undo is a stack of named
/// snapshots of the draft, which is the standard memento shape here and keeps
/// every edit trivially reversible.
/// </summary>
public sealed class RouteDraft
{
    public const int MaxUndo = 50;

    private sealed class UndoEntry
    {
        public string Label;
        public Route Snapshot;
    }

    private readonly List<UndoEntry> _undo = new List<UndoEntry>();

    public RouteDraft(Route source, bool isNew)
    {
        Original = source != null ? source.Clone() : new Route();
        Draft = source != null ? source.Clone() : new Route();
        IsNew = isNew;
        if (isNew && string.IsNullOrEmpty(Draft.Id)) Draft.Id = Route.NewId();
    }

    public Route Draft { get; private set; }
    public Route Original { get; }
    public bool IsNew { get; }

    /// <summary>Bumped on every snapshot or restore; the editor caches by it.</summary>
    public int Revision { get; private set; }

    public int UndoCount => _undo.Count;
    public bool CanUndo => _undo.Count > 0;
    public string LastLabel => _undo.Count == 0 ? "" : _undo[_undo.Count - 1].Label;

    public bool IsDirty => IsNew || !Draft.SameData(Original);

    /// <summary>Call before mutating Draft; label is shown as "Undo &lt;label&gt;".</summary>
    public void BeginChange(string label)
    {
        _undo.Add(new UndoEntry { Label = label, Snapshot = Draft.Clone() });
        while (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        Revision++;
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        var entry = _undo[_undo.Count - 1];
        _undo.RemoveAt(_undo.Count - 1);
        Draft = entry.Snapshot;
        Revision++;
        return true;
    }

    public void Cancel()
    {
        Draft = Original.Clone();
        _undo.Clear();
        Revision++;
    }
}
