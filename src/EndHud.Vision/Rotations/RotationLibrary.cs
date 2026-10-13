namespace EndHud.Vision.Rotations;

/// <summary>A rotation and where it comes from.</summary>
/// <param name="Published">One of EndHud's recommended rotations, kept up to date from GitHub
/// (<see cref="RotationSync"/>); the player's own otherwise.</param>
public sealed record LibraryRotation(Rotation Rotation, bool Published);

/// <summary>
/// Every rotation the player can use: their own (<see cref="Own"/>, the folder itself) and the
/// recommended ones published in the EndHud repository (<see cref="Published"/>, its "published"
/// subfolder, written only by <see cref="RotationSync"/>).
/// <list type="bullet">
/// <item>A published rotation is never edited in place: saving changes to one makes the player's own
/// copy (<see cref="RotationStore.AsCopy"/>), and the original keeps receiving updates.</item>
/// <item>Deleting a published rotation hides it: its file goes, and its id is remembered in
/// <c>published/hidden.txt</c> so the sync doesn't bring it back, until <see cref="ShowHidden"/>.</item>
/// <item>The sync never touches the player's own rotations.</item>
/// </list>
/// </summary>
public sealed class RotationLibrary(string folder)
{
    private const string HiddenFile = "hidden.txt";

    public RotationStore Own { get; } = new(folder);

    public RotationStore Published { get; } = new(Path.Combine(folder, "published"));

    /// <summary>Own rotations first, then the published ones (an own rotation with the same id wins).</summary>
    public (IReadOnlyList<LibraryRotation> Rotations, IReadOnlyList<UnreadableRotation> Unreadable) Load()
    {
        var (own, ownUnreadable) = Own.Load();
        var (published, publishedUnreadable) = Published.Load();
        var hidden = Hidden();
        var ownIds = own.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var rotations = own.Select(r => new LibraryRotation(r, false))
            .Concat(published.Where(r => !ownIds.Contains(r.Id) && !hidden.Contains(r.Id)).Select(r => new LibraryRotation(r, true)))
            .ToList();
        return (rotations, [.. ownUnreadable, .. publishedUnreadable]);
    }

    /// <summary>True if <paramref name="id"/> is a published rotation's id (shown or hidden).</summary>
    public bool IsPublished(string id) => Published.Exists(id) || Hidden().Contains(id);

    /// <summary>Delete one of the player's rotations, or hide a published one.</summary>
    public bool Delete(string id)
    {
        if (Own.Delete(id)) return true;
        if (!Published.Exists(id)) return false;
        Hide(id);
        return true;
    }

    /// <summary>The published rotations the player deleted.</summary>
    public IReadOnlySet<string> Hidden()
    {
        string path = Path.Combine(Published.Folder, HiddenFile);
        try
        {
            return File.Exists(path)
                ? File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
        }
        catch (IOException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private void Hide(string id)
    {
        var hidden = Hidden().Append(id).Distinct().Order(StringComparer.Ordinal);
        Directory.CreateDirectory(Published.Folder);
        File.WriteAllLines(Path.Combine(Published.Folder, HiddenFile), hidden);
        Published.Delete(id);
    }

    /// <summary>Forget which published rotations were deleted, so the next sync downloads them again.</summary>
    public void ShowHidden()
    {
        string path = Path.Combine(Published.Folder, HiddenFile);
        if (File.Exists(path)) File.Delete(path);
    }
}
