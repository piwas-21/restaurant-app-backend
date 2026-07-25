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
    Task<ImageBackfillReportDto> RunAsync(bool apply, int maxFiles, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete the dry-run preview folder. Previews are full-size copies, so they should not be
    /// left sitting on the uploads volume (or in the nightly backups) once they've been reviewed.
    /// Returns the number of files removed.
    /// </summary>
    int ClearPreviews();
}
