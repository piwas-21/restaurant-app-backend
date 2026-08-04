using RestaurantSystem.Api.Features.Maintenance.Dtos;

namespace RestaurantSystem.Api.Features.Maintenance.Interfaces;

/// <summary>
/// Applies the resize-on-upload pipeline to images that predate it. Uploads have been downscaled
/// at the storage seam since WS4A, but everything stored before then is still whatever the camera
/// produced — so the menu ships multi-megabyte originals to every guest.
/// </summary>
public interface IImageBackfillService
{
    /// <summary>
    /// Walk the uploads root and report what the pipeline would do to each image.
    ///
    /// <paramref name="apply"/> defaults off. A dry run writes each resized candidate to the
    /// preview folder and returns both URLs, so the result can be judged before any original is
    /// touched; only <c>apply: true</c> overwrites in place.
    /// </summary>
    /// <param name="apply">Overwrite the originals. Irreversible short of a restore from backup.</param>
    /// <param name="maxFiles">Stop after this many images, so one call can't run unbounded.</param>
    /// <param name="continueFrom">
    /// Resume point: process only images ordering strictly AFTER this relative path. Pass the
    /// previous report's <c>NextCursor</c> to walk a library larger than <paramref name="maxFiles"/>.
    /// Null starts from the beginning.
    ///
    /// <para>Without it the scan is not merely slow but INCAPABLE (#280): it always restarted from
    /// the first file, and the cap counts every file it processes, so image
    /// <paramref name="maxFiles"/>+1 could never be reached however many times it was called.</para>
    /// </param>
    Task<ImageBackfillReportDto> RunAsync(
        bool apply, int maxFiles, string? continueFrom = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete the dry-run preview folder. Previews are full-size copies, so they should not be
    /// left sitting on the uploads volume (or in the nightly backups) once they've been reviewed.
    /// Returns the number of files removed.
    /// </summary>
    int ClearPreviews();
}
