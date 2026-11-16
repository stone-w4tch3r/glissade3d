using System;
using System.Collections.Generic;
using System.Text;

namespace Glissade3D;

public sealed class SharedInstallResult
{
    public int Installed;
    public int Updated;
    public int UpToDate;
    public int SkippedModified;
    public int SkippedConflict;
    public int Failed;

    public readonly List<string> Notes = new List<string>();
    public int Skipped => SkippedModified + SkippedConflict;
    public bool Changed => Installed + Updated > 0;
}

public sealed class SharedUninstallResult
{
    public int Removed;
    public int KeptModified;
    public int Failed;

    public readonly List<string> Notes = new List<string>();
}

/// <summary>
/// Install/uninstall shared routes with package-manager semantics: the id is the
/// identity, a route is only overwritten or deleted while it still matches the
/// hash we installed, and user edits are never touched.
/// </summary>
public static class SharedInstaller
{
    public const string ManifestName = "index.cfg";

    /// <summary>Set when the public route repository is published.</summary>
    public const string DefaultBaseUrl = "";

    /// <summary>
    /// ConfigNode treats // as a comment, so a URL cannot be stored raw in a cfg.
    /// Settings keep the percent-encoded form and decode it on load.
    /// </summary>
    public static string EncodeBaseUrl(string url)
    {
        return string.IsNullOrWhiteSpace(url) ? "" : Uri.EscapeDataString(url.Trim());
    }

    public static string DecodeBaseUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        try { return Uri.UnescapeDataString(value.Trim()); }
        catch (Exception) { return ""; }
    }

    public static string NormalizeBaseUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        var text = url.Trim();
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return "";
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return "";
        if (string.IsNullOrEmpty(uri.Host)) return "";
        return text.EndsWith("/", StringComparison.Ordinal) ? text : text + "/";
    }

    public static SharedInstallResult Install(SharedCatalog catalog, IDictionary<string, byte[]> files,
        RouteStorage storage, IList<Route> routes, SharedInstallState state, string statePath)
    {
        var result = new SharedInstallResult();
        if (catalog == null || files == null || storage == null || routes == null || state == null)
        {
            result.Failed++;
            result.Notes.Add("missing arguments");
            return result;
        }

        foreach (var entry in catalog.Entries)
        {
            if (!files.TryGetValue(entry.File, out var bytes) || bytes == null)
            {
                result.Failed++;
                result.Notes.Add("not downloaded: " + entry.File);
                continue;
            }

            if (!string.Equals(SharedHash.Bytes(bytes), entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                result.Failed++;
                result.Notes.Add("hash mismatch: " + entry.File);
                continue;
            }

            var route = ParseRoute(bytes);
            if (route == null || route.Id != entry.Id)
            {
                result.Failed++;
                result.Notes.Add("invalid route: " + entry.File);
                continue;
            }

            var existing = Find(routes, route.Id);
            var managed = state.TryGet(route.Id, out var installed);

            if (existing != null && !managed)
            {
                result.SkippedConflict++;
                result.Notes.Add("id conflict: " + route.Id);
                continue;
            }

            if (managed && existing != null)
            {
                var currentHash = SharedHash.Of(existing);
                if (!string.Equals(currentHash, installed.Hash, StringComparison.Ordinal))
                {
                    result.SkippedModified++;
                    result.Notes.Add("modified, kept: " + route.Name);
                    continue;
                }

                var remoteHash = SharedHash.Of(route);
                if (string.Equals(remoteHash, installed.Hash, StringComparison.Ordinal))
                {
                    result.UpToDate++;
                    continue;
                }

                if (!storage.Save(route, out var updateError))
                {
                    result.Failed++;
                    result.Notes.Add("save failed: " + updateError);
                    continue;
                }

                installed.Name = route.Name;
                installed.Hash = remoteHash;
                result.Updated++;
                continue;
            }

            if (!storage.Save(route, out var saveError))
            {
                result.Failed++;
                result.Notes.Add("save failed: " + saveError);
                continue;
            }

            state.Set(route.Id, route.Name, SharedHash.Of(route));
            routes.Add(route);
            result.Installed++;
        }

        if (!state.Save(statePath, out var stateError))
        {
            result.Failed++;
            result.Notes.Add("state save failed: " + stateError);
        }

        return result;
    }

    public static SharedUninstallResult Uninstall(RouteStorage storage, IList<Route> routes,
        SharedInstallState state, string statePath)
    {
        var result = new SharedUninstallResult();
        if (storage == null || routes == null || state == null)
        {
            result.Failed++;
            return result;
        }

        var ids = new List<string>();
        foreach (var entry in state.Entries) ids.Add(entry.Id);

        foreach (var id in ids)
        {
            if (!state.TryGet(id, out var installed)) continue;

            var route = Find(routes, id);
            if (route == null)
            {
                state.Remove(id);
                continue;
            }

            if (!string.Equals(SharedHash.Of(route), installed.Hash, StringComparison.Ordinal))
            {
                result.KeptModified++;
                result.Notes.Add("kept modified: " + route.Name);
                state.Remove(id);
                continue;
            }

            if (!storage.Delete(route, out var error))
            {
                result.Failed++;
                result.Notes.Add("delete failed: " + error);
                continue;
            }

            state.Remove(id);
            result.Removed++;
        }

        if (!state.Save(statePath, out var stateError))
        {
            result.Failed++;
            result.Notes.Add("state save failed: " + stateError);
        }

        return result;
    }

    private static Route Find(IList<Route> routes, string id)
    {
        if (routes == null || string.IsNullOrEmpty(id)) return null;
        foreach (var route in routes)
        {
            if (string.Equals(route.Id, id, StringComparison.Ordinal)) return route;
        }
        return null;
    }

    private static Route ParseRoute(byte[] bytes)
    {
        try
        {
            var text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
            var root = ConfigNode.Parse(text);
            if (root == null) return null;
            var node = root.name == "Route" ? root : root.GetNode("Route");
            return node == null ? null : Route.ReadFrom(node);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
