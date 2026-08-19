// FILE_LENGTH_EXEMPT: a lookup table, not a type with behaviour — 28 colour ROLES × 2 schemes, and
// the two initialisers are the data. The §4 record limit is there to stop a DTO growing logic;
// splitting this into five 20-line files (surface / table / notice / action / chrome) would scatter
// one table a reader wants to read down a single column, and every one of those files would still
// be a list of assignments. The behaviour that uses it is in the templates, which the file-length
// gate measures separately (#356).
namespace RestaurantSystem.Api.Common.Templates;

public static partial class EmailTemplates
{
    /// <summary>
    /// The colours one of the two-scheme operator mails is painted in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A mail client cannot be asked which scheme it is in, so these templates ship BOTH and let a
    /// <c>prefers-color-scheme</c> media query show one — which is why the same 100-line block used
    /// to appear twice per template, differing only in its colours. That is what
    /// <c>sonar.cpd.exclusions</c> was hiding (backend #356), and hiding it meant genuine future
    /// duplication in a template would not be reported either.
    /// </para>
    /// <para>
    /// Every value here is one the templates already carried; this type renames them, it does not
    /// choose them. A field is a ROLE rather than a colour — the same light value maps to two
    /// different dark ones depending on what it paints (<c>#ffffff</c> is the page behind
    /// <see cref="PageBackground"/> and the items table behind <see cref="TableBackground"/>), so a
    /// find-and-replace on the hex would have been wrong.
    /// </para>
    /// </remarks>
    internal sealed record EmailPalette
    {
        public required string ModeName { get; init; }
        public required string ModeClass { get; init; }
        public required string PageBackground { get; init; }
        public required string HeaderGradientFrom { get; init; }
        public required string HeaderGradientTo { get; init; }
        public required string ConfirmGradientFrom { get; init; }
        public required string ConfirmGradientTo { get; init; }
        public required string OrderBadgeShadow { get; init; }
        public required string SurfaceBackground { get; init; }
        public required string SurfaceBorder { get; init; }
        public required string StrongText { get; init; }
        public required string MutedText { get; init; }
        public required string TotalText { get; init; }
        public required string TableBackground { get; init; }
        public required string TableHeadBackground { get; init; }
        public required string TableHeadText { get; init; }
        public required string NoticeBackground { get; init; }
        public required string NoticeBorder { get; init; }
        public required string NoticeHeading { get; init; }
        public required string NoticeText { get; init; }
        public required string ConfirmButtonShadow { get; init; }
        public required string TimeButtonBackground { get; init; }
        public required string DashboardButtonBackground { get; init; }
        public required string CancelButtonShadow { get; init; }
        public required string FooterBackground { get; init; }
        public required string FooterLink { get; init; }
        public required string FooterText { get; init; }
        public required string ReservationBadgeGradientFrom { get; init; }
        public required string ReservationBadgeGradientTo { get; init; }
        public required string ReservationBadgeShadow { get; init; }

        /// <summary>What a client with no dark preference shows.</summary>
        public static readonly EmailPalette Light = new()
        {
            ModeName = "Light",
            ModeClass = "light-only",
            PageBackground = "#ffffff",
            HeaderGradientFrom = "#d4af37",
            HeaderGradientTo = "#f4c430",
            ConfirmGradientFrom = "#10b981",
            ConfirmGradientTo = "#059669",
            OrderBadgeShadow = "rgba(16, 185, 129, 0.2)",
            SurfaceBackground = "#f9fafb",
            SurfaceBorder = "#e5e7eb",
            StrongText = "#111827",
            MutedText = "#6b7280",
            TotalText = "#059669",
            TableBackground = "#ffffff",
            TableHeadBackground = "#f9fafb",
            TableHeadText = "#6b7280",
            NoticeBackground = "#fef3c7",
            NoticeBorder = "#fbbf24",
            NoticeHeading = "#92400e",
            NoticeText = "#78350f",
            ConfirmButtonShadow = "rgba(16, 185, 129, 0.3)",
            TimeButtonBackground = "#7fa89bff",
            DashboardButtonBackground = "#3b82f6",
            CancelButtonShadow = "rgba(220, 38, 38, 0.3)",
            FooterBackground = "#f3f4f6",
            FooterLink = "#3b82f6",
            FooterText = "#9ca3af",
            ReservationBadgeGradientFrom = "#8b5cf6",
            ReservationBadgeGradientTo = "#7c3aed",
            ReservationBadgeShadow = "rgba(139, 92, 246, 0.2)",
        };

        /// <summary>What a client in dark mode shows.</summary>
        public static readonly EmailPalette Dark = new()
        {
            ModeName = "Dark",
            ModeClass = "dark-only",
            PageBackground = "#1f2937",
            HeaderGradientFrom = "#b8941f",
            HeaderGradientTo = "#d4af37",
            ConfirmGradientFrom = "#059669",
            ConfirmGradientTo = "#047857",
            OrderBadgeShadow = "rgba(5, 150, 105, 0.3)",
            SurfaceBackground = "#374151",
            SurfaceBorder = "#4b5563",
            StrongText = "#f9fafb",
            MutedText = "#9ca3af",
            TotalText = "#34d399",
            TableBackground = "#374151",
            TableHeadBackground = "#4b5563",
            TableHeadText = "#d1d5db",
            NoticeBackground = "#78350f",
            NoticeBorder = "#f59e0b",
            NoticeHeading = "#fef3c7",
            NoticeText = "#fde68a",
            ConfirmButtonShadow = "rgba(5, 150, 105, 0.4)",
            TimeButtonBackground = "#6b9688",
            DashboardButtonBackground = "#2563eb",
            CancelButtonShadow = "rgba(220, 38, 38, 0.4)",
            FooterBackground = "#374151",
            FooterLink = "#60a5fa",
            FooterText = "#6b7280",
            ReservationBadgeGradientFrom = "#7c3aed",
            ReservationBadgeGradientTo = "#6d28d9",
            ReservationBadgeShadow = "rgba(124, 58, 237, 0.3)",
        };
    }
}
