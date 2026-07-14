using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Llamashot.Core.VideoEditor;

/// <summary>A recent-project entry persisted in recentProjects.json.</summary>
public record RecentProject(
    string Path,
    string Name,
    int Width,
    int Height,
    int Fps,
    double DurationSec,
    string? ThumbPath,
    DateTime Modified);

/// <summary>
/// Persists a "recent projects" list to the app data directory.
/// The list is capped at 30 entries, most-recent first, and deduped by path
/// (case-insensitive).
///
/// Test redirection: set <see cref="DataDirOverride"/> to a temp folder before
/// calling any method.  When non-null it replaces the default
/// %AppData%\Llamashot base, so automated tests never touch real user data.
/// </summary>
public static class ProjectLibrary
{
    private const int MaxEntries = 30;
    private const string FileName = "recentProjects.json";

    /// <summary>
    /// When non-null, overrides the default %AppData%\Llamashot data directory.
    /// Set this to a temp folder in tests/harness BEFORE calling any method.
    /// </summary>
    public static string? DataDirOverride { get; set; }

    private static string DataDir =>
        DataDirOverride
        ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Llamashot");

    private static string FilePath => System.IO.Path.Combine(DataDir, FileName);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // ------------------------------------------------------------------ Load
    /// <summary>Loads the recent-projects list. Returns an empty list if the file
    /// is missing or corrupt — never throws.</summary>
    public static IReadOnlyList<RecentProject> Load()
    {
        try
        {
            string path = FilePath;
            if (!File.Exists(path)) return [];
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<RecentProject>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    // ------------------------------------------------------------------- Add
    /// <summary>Inserts or updates an entry (deduped by path, case-insensitive),
    /// keeps the list most-recent first, caps at 30, and persists immediately.</summary>
    public static void Add(RecentProject p)
    {
        var list = new List<RecentProject>(Load());

        // Remove any existing entry for the same path (case-insensitive).
        list.RemoveAll(e =>
            string.Equals(e.Path, p.Path, StringComparison.OrdinalIgnoreCase));

        // Prepend the new/updated entry.
        list.Insert(0, p);

        // Cap at MaxEntries.
        if (list.Count > MaxEntries)
            list = list[..MaxEntries];

        Persist(list);
    }

    // ---------------------------------------------------------------- Remove
    /// <summary>Removes the entry with the given path and persists.</summary>
    public static void Remove(string path)
    {
        var list = new List<RecentProject>(Load());
        list.RemoveAll(e =>
            string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
        Persist(list);
    }

    // ------------------------------------------------------------ Persist
    private static void Persist(IReadOnlyList<RecentProject> list)
    {
        Directory.CreateDirectory(DataDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(list, JsonOptions));
    }
}
