using System.Diagnostics;

namespace RestaurantSystem.Api.Common
{
    /// <summary>
    /// Build and runtime identity for the running API instance.
    /// <para>
    /// Commit SHA and build time are baked into the container image at docker-build
    /// time (the <c>GIT_SHA</c> / <c>BUILD_TIME</c> environment variables, set from
    /// CI build-args — see build-image.yml). Outside a CI-built image (local <c>dotnet
    /// run</c>) they read back as <c>"unknown"</c>.
    /// </para>
    /// </summary>
    public static class BuildInfo
    {
        /// <summary>Full git commit SHA the image was built from, or "unknown".</summary>
        public static string CommitSha =>
            Environment.GetEnvironmentVariable("GIT_SHA") is { Length: > 0 } sha ? sha : "unknown";

        /// <summary>First 7 chars of <see cref="CommitSha"/> (the short SHA), or "unknown".</summary>
        public static string ShortCommitSha =>
            CommitSha.Length >= 7 ? CommitSha[..7] : CommitSha;

        /// <summary>ISO-8601 UTC timestamp of when the image was built, or "unknown".</summary>
        public static string BuildTime =>
            Environment.GetEnvironmentVariable("BUILD_TIME") is { Length: > 0 } t ? t : "unknown";

        /// <summary>UTC time this process started (used to compute uptime).</summary>
        public static DateTime ProcessStartUtc { get; } =
            Process.GetCurrentProcess().StartTime.ToUniversalTime();

        /// <summary>Wall-clock time since the process started.</summary>
        public static TimeSpan Uptime => DateTime.UtcNow - ProcessStartUtc;
    }
}
