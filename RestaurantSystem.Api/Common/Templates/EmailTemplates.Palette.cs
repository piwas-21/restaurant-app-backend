// FILE_LENGTH_EXEMPT: a lookup table, not a type with behaviour — 28 colour roles, each written
// once with both of its values. The §4 record limit exists to stop a DTO growing logic, and there is
// none here. Splitting one table a reader reads down a single column into five files of assignments
// would cost the only thing this file has going for it (#356).
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
    /// Each role is declared ONCE, with its light and dark value side by side, and the two instances
    /// differ only in which half of the pair they take. Two separate initialiser lists would be the
    /// same duplication one level up — and a reader would have to hold thirty lines in their head to
    /// compare a colour with its counterpart.
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
        public string ModeName { get; }
        public string ModeClass { get; }
        public string PageBackground { get; }
        public string HeaderGradientFrom { get; }
        public string HeaderGradientTo { get; }
        public string ConfirmGradientFrom { get; }
        public string ConfirmGradientTo { get; }
        public string OrderBadgeShadow { get; }
        public string SurfaceBackground { get; }
        public string SurfaceBorder { get; }
        public string StrongText { get; }
        public string MutedText { get; }
        public string TotalText { get; }
        public string TableBackground { get; }
        public string TableHeadBackground { get; }
        public string TableHeadText { get; }
        public string NoticeBackground { get; }
        public string NoticeBorder { get; }
        public string NoticeHeading { get; }
        public string NoticeText { get; }
        public string ConfirmButtonShadow { get; }
        public string TimeButtonBackground { get; }
        public string DashboardButtonBackground { get; }
        public string CancelButtonShadow { get; }
        public string FooterBackground { get; }
        public string FooterLink { get; }
        public string FooterText { get; }
        public string ReservationBadgeGradientFrom { get; }
        public string ReservationBadgeGradientTo { get; }
        public string ReservationBadgeShadow { get; }

        private EmailPalette(string modeName, string modeClass, Func<(string Light, string Dark), string> pick)
        {
            ModeName = modeName;
            ModeClass = modeClass;
            PageBackground = pick(("#ffffff", "#1f2937"));
            HeaderGradientFrom = pick(("#d4af37", "#b8941f"));
            HeaderGradientTo = pick(("#f4c430", "#d4af37"));
            ConfirmGradientFrom = pick(("#10b981", "#059669"));
            ConfirmGradientTo = pick(("#059669", "#047857"));
            OrderBadgeShadow = pick(("rgba(16, 185, 129, 0.2)", "rgba(5, 150, 105, 0.3)"));
            SurfaceBackground = pick(("#f9fafb", "#374151"));
            SurfaceBorder = pick(("#e5e7eb", "#4b5563"));
            StrongText = pick(("#111827", "#f9fafb"));
            MutedText = pick(("#6b7280", "#9ca3af"));
            TotalText = pick(("#059669", "#34d399"));
            TableBackground = pick(("#ffffff", "#374151"));
            TableHeadBackground = pick(("#f9fafb", "#4b5563"));
            TableHeadText = pick(("#6b7280", "#d1d5db"));
            NoticeBackground = pick(("#fef3c7", "#78350f"));
            NoticeBorder = pick(("#fbbf24", "#f59e0b"));
            NoticeHeading = pick(("#92400e", "#fef3c7"));
            NoticeText = pick(("#78350f", "#fde68a"));
            ConfirmButtonShadow = pick(("rgba(16, 185, 129, 0.3)", "rgba(5, 150, 105, 0.4)"));
            TimeButtonBackground = pick(("#7fa89bff", "#6b9688"));
            DashboardButtonBackground = pick(("#3b82f6", "#2563eb"));
            CancelButtonShadow = pick(("rgba(220, 38, 38, 0.3)", "rgba(220, 38, 38, 0.4)"));
            FooterBackground = pick(("#f3f4f6", "#374151"));
            FooterLink = pick(("#3b82f6", "#60a5fa"));
            FooterText = pick(("#9ca3af", "#6b7280"));
            ReservationBadgeGradientFrom = pick(("#8b5cf6", "#7c3aed"));
            ReservationBadgeGradientTo = pick(("#7c3aed", "#6d28d9"));
            ReservationBadgeShadow = pick(("rgba(139, 92, 246, 0.2)", "rgba(124, 58, 237, 0.3)"));
        }

        /// <summary>What a client with no dark preference shows.</summary>
        public static readonly EmailPalette Light = new("Light", "light-only", scheme => scheme.Light);

        /// <summary>What a client in dark mode shows.</summary>
        public static readonly EmailPalette Dark = new("Dark", "dark-only", scheme => scheme.Dark);
    }
}
