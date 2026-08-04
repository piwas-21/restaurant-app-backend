namespace RestaurantSystem.Api.Features.Maintenance.Services;

/// <summary>
/// Decides WHICH files a backfill run considers, in what order, and where a run resumes. Split out
/// of <see cref="ImageBackfillService"/> when #280 gave the walk a cursor: that made the ordering
/// contract load-bearing rather than incidental, and it is now the only thing standing between a
/// paged run and silently skipped images.
/// </summary>
internal static class ImageBackfillScanner
{
    private static readonly string[] ScannedExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    /// <summary>
    /// The images to consider, in a total and stable order, each paired with the relative path that
    /// identifies it to a caller.
    ///
    /// <para><b>Ordered and resumed on the RELATIVE path, never the absolute one.</b> They are not
    /// interchangeable: the relative form is separator-normalized to '/' (0x2F) while a Windows
    /// absolute path carries '\' (0x5C), so the two orderings diverge the moment a subdirectory is
    /// involved — <c>a/b.jpg</c> vs <c>a-1.jpg</c> sort one way as relative paths and the other way
    /// as Windows absolute ones. Sorting by one and resuming on the other would skip or repeat
    /// files at directory boundaries, on Windows only. Keeping the sort key and the cursor as the
    /// same string is what makes that impossible rather than merely unlikely.</para>
    ///
    /// <para>The preview folder is excluded so repeat runs never recurse into their own output.</para>
    /// </summary>
    /// <param name="continueFrom">
    /// Resume point. Files ordering STRICTLY AFTER it are returned — it names the last file already
    /// done, so including it would re-process one image per call and, at a cap of 1, never advance.
    /// Null starts from the beginning.
    /// </param>
    public static IEnumerable<(string Path, string RelativePath)> EnumerateImages(
        string uploadsRoot, string previewFolder, string? continueFrom)
    {
        var previewRoot = Path.Combine(uploadsRoot, previewFolder);

        var candidates = Directory
            .EnumerateFiles(uploadsRoot, "*", SearchOption.AllDirectories)
            .Where(p => !p.StartsWith(previewRoot, StringComparison.Ordinal))
            .Where(p => ScannedExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
            .Select(p => (Path: p, RelativePath: ToRelativePath(uploadsRoot, p)))
            .OrderBy(x => x.RelativePath, StringComparer.Ordinal);

        return string.IsNullOrEmpty(continueFrom)
            ? candidates
            : candidates.Where(x => string.CompareOrdinal(x.RelativePath, continueFrom) > 0);
    }

    /// <summary>The '/'-normalized path under the uploads root — the id a caller sees and resumes on.</summary>
    public static string ToRelativePath(string uploadsRoot, string path) =>
        Path.GetRelativePath(uploadsRoot, path).Replace('\\', '/');
}
