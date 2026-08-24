using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using FluentAssertions;
using RestaurantSystem.Api.Common.Templates;

namespace RestaurantSystem.IntegrationTests.Common.Templates;

/// <summary>
/// Byte-for-byte snapshot of every templated email, rendered in the neutral (English)
/// culture (EMAIL-LOCALISATION-PLAN §5 S1).
/// <para>
/// The committed <c>Golden/*.txt</c> files were generated from the code as it stood
/// BEFORE the strings moved into <c>Resources/Email/*.resx</c>, so this suite is the
/// proof that the move changed no output. The only deliberate exceptions are the three
/// <c>MembershipConfirmation</c> snapshots: that mail was inline in <c>EmailService</c>
/// and lost its two dead "Add to Wallet" buttons (both <c>href="#"</c>, captioned
/// "coming soon") in the same move.
/// </para>
/// <para>
/// Rendering forces the invariant ambient culture so a snapshot does not depend on the
/// machine's locale: amounts and dates are still ambient-formatted, which this slice
/// deliberately does not change (§6.2 — currency stays a LocalizationSettings label and
/// is never derived from the culture).
/// </para>
/// <para>
/// To re-record after an intentional copy change: <c>EMAIL_GOLDEN_UPDATE=1 dotnet test
/// --filter FullyQualifiedName~EmailTemplateGoldenTests</c>, then read the diff.
/// </para>
/// </summary>
public class EmailTemplateGoldenTests
{
    private static readonly CultureInfo Culture = EmailCultures.English;

    private static readonly EmailBranding Brand = new("Demo Restaurant", "Geneva", "contact@demo.test");
    private static readonly (string name, int quantity, decimal price)[] Items =
        [("Burger", 2, 12.50m), ("Fries", 1, 4.25m)];
    private static readonly DateTime Moment = new(2030, 5, 17, 19, 30, 0, DateTimeKind.Utc);

    /// <summary>
    /// The same instant on the tenant's wall clock, which is what a mail prints (#363). The offset
    /// is fixed rather than resolved from <c>Europe/Zurich</c> on purpose: a golden that asked the
    /// host for a zone would re-record itself twice a year at the DST boundary, and would render
    /// differently on a machine whose tzdata is missing.
    /// </summary>
    private static readonly DateTimeOffset MomentLocal =
        new(DateTime.SpecifyKind(Moment.AddHours(2), DateTimeKind.Unspecified), TimeSpan.FromHours(2));
    private static readonly TimeSpan StartTime = new(19, 30, 0);
    private static readonly TimeSpan EndTime = new(21, 0, 0);
    private static readonly Guid ReservationId = new("11111111-2222-3333-4444-555555555555");
    private const decimal Total = 25.00m;
    private const string CustomerName = "Jane Doe";
    private const string CustomerEmail = "jane@demo.test";
    private const string CustomerPhone = "+41000000";
    private const string ContactEmail = "admin@demo.test";
    private const string OrderNumber = "ORD-1";
    private const string TableNumber = "T12";
    private const string Instructions = "No onions";
    private const string DeliveryAddress = "Rue de Test 1";
    private const string SpecialRequests = "Window seat";

    /// <summary>
    /// The same fixture values, grouped as the templates now take them (#355). Declared once here
    /// rather than inline per call, which is what a parameter object is for.
    /// </summary>
    private static readonly EmailGuest Guest = new(CustomerName, CustomerEmail, CustomerPhone);
    private static readonly EmailLinks Links = new(ApiBaseUrl, FrontendBaseUrl, ContactEmail);
    private static readonly OrderMailDetails OrderDetails = new(
        OrderNumber, "Delivery", Total, Items, "CHF", "quick-action-token", Instructions, DeliveryAddress);
    // The two link signatures are fixed strings, not real ones: a golden that minted a token would
    // re-record itself on every run (the signature covers a wall-clock expiry). What the snapshot
    // has to pin is that the buttons carry ONE, and where (backend #402).
    private static readonly ReservationMailDetails ReservationDetails = new(
        Moment, StartTime, EndTime, 4, TableNumber, SpecialRequests, ReservationId,
        "approve-token-fixture", "reject-token-fixture");
    private const string RestaurantNote = "See you soon";
    private const string ApiBaseUrl = "https://api.demo.test";
    private const string FrontendBaseUrl = "https://demo.test";
    private const string DeleteUrl = "https://demo.test/delete";
    private const string CancelUrl = "https://demo.test/cancel";
    private const string VerifyUrl = "https://demo.test/verify";
    private const string ResetUrl = "https://demo.test/reset";
    private const string ApproveUrl = "https://demo.test/approve";
    private const string RejectUrl = "https://demo.test/reject";
    private const string GroupName = "Gold Club";
    private const string GroupDescription = "Members get 10% off.";
    private const string QrCodeData = "MEMBER-123";

    private static IEnumerable<(string Name, string Rendered)> RenderAll()
    {
        yield return ("AccountDeletion.subject", EmailTemplates.AccountDeletion.GetSubject(Culture, Brand));
        yield return ("AccountDeletion.html", EmailTemplates.AccountDeletion.GetHtmlBody(Culture, Brand, "Jane", "Doe", DeleteUrl, CancelUrl, Moment));
        yield return ("AccountDeletion.text", EmailTemplates.AccountDeletion.GetTextBody(Culture, Brand, "Jane", "Doe", DeleteUrl, CancelUrl, Moment));
        yield return ("EmailVerification.subject", EmailTemplates.EmailVerification.GetSubject(Culture, Brand));
        yield return ("EmailVerification.html", EmailTemplates.EmailVerification.GetHtmlBody(Culture, Brand, "Jane", "Doe", VerifyUrl));
        yield return ("EmailVerification.text", EmailTemplates.EmailVerification.GetTextBody(Culture, Brand, "Jane", "Doe", VerifyUrl));
        yield return ("OrderCancelled.subject", EmailTemplates.OrderCancelled.GetSubject(Culture, Brand));
        yield return ("OrderCancelled.html", EmailTemplates.OrderCancelled.GetHtmlBody(Culture, Brand, CustomerName, OrderNumber, "Kitchen closed unexpectedly", ContactEmail));
        yield return ("OrderCancelled.text", EmailTemplates.OrderCancelled.GetTextBody(Culture, Brand, CustomerName, OrderNumber, "Kitchen closed unexpectedly", ContactEmail));
        yield return ("OrderConfirmationAdmin.subject", EmailTemplates.OrderConfirmationAdmin.GetSubject(Culture, Brand));
        yield return ("OrderConfirmationAdmin.html", EmailTemplates.OrderConfirmationAdmin.GetHtmlBody(Culture, Brand, Guest, OrderDetails, Links));
        yield return ("OrderConfirmationAdmin.text", EmailTemplates.OrderConfirmationAdmin.GetTextBody(Culture, Brand, Guest, OrderDetails, ContactEmail));
        yield return ("OrderConfirmed.subject", EmailTemplates.OrderConfirmed.GetSubject(Culture, Brand));
        yield return ("OrderConfirmed.html", EmailTemplates.OrderConfirmed.GetHtmlBody(Culture, Brand, CustomerName, OrderNumber, "Takeaway", 30, ContactEmail));
        yield return ("OrderConfirmed.text", EmailTemplates.OrderConfirmed.GetTextBody(Culture, Brand, CustomerName, OrderNumber, "Takeaway", 30, ContactEmail));
        yield return ("OrderDelayed.subject", EmailTemplates.OrderDelayed.GetSubject(Culture, Brand));
        yield return ("OrderDelayed.html", EmailTemplates.OrderDelayed.GetHtmlBody(Culture, Brand, CustomerName, OrderNumber, 25, ApproveUrl, RejectUrl, ContactEmail));
        yield return ("OrderDelayed.text", EmailTemplates.OrderDelayed.GetTextBody(Culture, Brand, CustomerName, OrderNumber, 25, ApproveUrl, RejectUrl, ContactEmail));
        yield return ("OrderReceived.subject", EmailTemplates.OrderReceived.GetSubject(Culture, Brand));
        yield return ("OrderReceived.html", EmailTemplates.OrderReceived.GetHtmlBody(
            Culture, Brand, CustomerName, new OrderMailDetails(OrderNumber, "Delivery", Total, Items, "CHF", SpecialInstructions: Instructions, DeliveryAddress: DeliveryAddress), ContactEmail));
        yield return ("OrderReceived.text", EmailTemplates.OrderReceived.GetTextBody(
            Culture, Brand, CustomerName, new OrderMailDetails(OrderNumber, "Delivery", Total, Items, "CHF", SpecialInstructions: Instructions, DeliveryAddress: DeliveryAddress), ContactEmail));
        yield return ("PasswordChanged.subject", EmailTemplates.PasswordChanged.GetSubject(Culture, Brand));
        yield return ("PasswordChanged.html", EmailTemplates.PasswordChanged.GetHtmlBody(Culture, Brand, "Jane", "Doe", MomentLocal));
        yield return ("PasswordChanged.text", EmailTemplates.PasswordChanged.GetTextBody(Culture, Brand, "Jane", "Doe", MomentLocal));
        yield return ("PasswordReset.subject", EmailTemplates.PasswordReset.GetSubject(Culture, Brand));
        yield return ("PasswordReset.html", EmailTemplates.PasswordReset.GetHtmlBody(Culture, Brand, "Jane", "Doe", ResetUrl));
        yield return ("PasswordReset.text", EmailTemplates.PasswordReset.GetTextBody(Culture, Brand, "Jane", "Doe", ResetUrl));
        yield return ("ReservationAdminNotification.subject", EmailTemplates.ReservationAdminNotification.GetSubject(Culture, Brand));
        yield return ("ReservationAdminNotification.html", EmailTemplates.ReservationAdminNotification.GetHtmlBody(Culture, Brand, Guest, ReservationDetails, Links));
        yield return ("ReservationAdminNotification.text", EmailTemplates.ReservationAdminNotification.GetTextBody(Culture, Brand, Guest, ReservationDetails, ContactEmail));
        yield return ("ReservationApproved.subject", EmailTemplates.ReservationApproved.GetSubject(Culture, Brand));
        yield return ("ReservationApproved.html", EmailTemplates.ReservationApproved.GetHtmlBody(
            Culture, Brand, CustomerName, new ReservationMailDetails(Moment, StartTime, EndTime, 4, TableNumber, SpecialRequests), ContactEmail, RestaurantNote));
        yield return ("ReservationApproved.text", EmailTemplates.ReservationApproved.GetTextBody(
            Culture, Brand, CustomerName, new ReservationMailDetails(Moment, StartTime, EndTime, 4, TableNumber, SpecialRequests), ContactEmail, RestaurantNote));
        yield return ("ReservationConfirmation.subject", EmailTemplates.ReservationConfirmation.GetSubject(Culture, Brand));
        yield return ("ReservationConfirmation.html", EmailTemplates.ReservationConfirmation.GetHtmlBody(
            Culture, Brand, CustomerName, new ReservationMailDetails(Moment, StartTime, EndTime, 4, TableNumber, SpecialRequests), ContactEmail));
        yield return ("ReservationConfirmation.text", EmailTemplates.ReservationConfirmation.GetTextBody(
            Culture, Brand, CustomerName, new ReservationMailDetails(Moment, StartTime, EndTime, 4, TableNumber, SpecialRequests), ContactEmail));
        yield return ("ReservationRejected.subject", EmailTemplates.ReservationRejected.GetSubject(Culture, Brand));
        yield return ("ReservationRejected.html", EmailTemplates.ReservationRejected.GetHtmlBody(Culture, Brand, CustomerName, Moment, StartTime, 4, ContactEmail));
        yield return ("ReservationRejected.text", EmailTemplates.ReservationRejected.GetTextBody(Culture, Brand, CustomerName, Moment, StartTime, 4, ContactEmail));
        yield return ("Welcome.subject", EmailTemplates.Welcome.GetSubject(Culture, Brand));
        yield return ("Welcome.html", EmailTemplates.Welcome.GetHtmlBody(Culture, Brand, "Jane", "Doe", "Customer"));
        yield return ("Welcome.text", EmailTemplates.Welcome.GetTextBody(Culture, Brand, "Jane", "Doe", "Customer"));
        yield return ("MembershipConfirmation.subject", EmailTemplates.MembershipConfirmation.GetSubject(Culture, GroupName));
        yield return ("MembershipConfirmation.html", EmailTemplates.MembershipConfirmation.GetHtmlBody(Culture, Brand, CustomerName, GroupName, GroupDescription, Moment));
        yield return ("MembershipConfirmation.text", EmailTemplates.MembershipConfirmation.GetTextBody(Culture, Brand, CustomerName, GroupName, GroupDescription, QrCodeData, Moment));
    }

    public static TheoryData<string> TemplateNames
    {
        get
        {
            var names = new TheoryData<string>();
            foreach (var name in Rendered().Keys.Order(StringComparer.Ordinal))
            {
                names.Add(name);
            }

            return names;
        }
    }

    [Theory]
    [MemberData(nameof(TemplateNames))]
    public void Template_RendersByteIdenticalToGolden(string name)
    {
        var actual = Tokenise(Rendered()[name]);
        var goldenFile = Path.Combine(GoldenDirectory(), name + ".txt");

        File.Exists(goldenFile).Should().BeTrue($"the golden snapshot {name}.txt must be committed, not silently absent");
        actual.Should().Be(ReadGolden(goldenFile), $"{name} must render exactly as it did before the strings moved into .resx");
    }

    [Fact]
    public void EveryTemplate_HasAGoldenSnapshot()
    {
        var expected = Rendered().Keys.Order(StringComparer.Ordinal);
        var onDisk = Directory.EnumerateFiles(GoldenDirectory(), "*.txt")
            .Select(Path.GetFileNameWithoutExtension)
            .Order(StringComparer.Ordinal);

        onDisk.Should().Equal(expected);
    }

    /// <summary>
    /// Renders under the invariant ambient culture so the snapshot does not depend on the
    /// machine's locale.
    /// </summary>
    private static Dictionary<string, string> Rendered()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try
        {
            return RenderAll().ToDictionary(x => x.Name, x => x.Rendered, StringComparer.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    /// <summary>The footer carries the current year, so it is tokenised rather than frozen.</summary>
    private static string Tokenise(string rendered) =>
        rendered.Replace(DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture), "{YEAR}", StringComparison.Ordinal);

    /// <summary>
    /// Reads a snapshot. The files are stored with one trailing newline that no template
    /// renders: the repo's <c>end-of-file-fixer</c> pre-commit hook would add it anyway, and a
    /// hook silently editing the safety net is worse than owning the convention here.
    /// </summary>
    private static string ReadGolden(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        return text.EndsWith('\n') ? text[..^1] : text;
    }

    private static string GoldenDirectory([CallerFilePath] string sourceFile = "") =>
        Path.Combine(Path.GetDirectoryName(sourceFile)!, "Golden");

    /// <summary>
    /// Re-records the snapshots when <c>EMAIL_GOLDEN_UPDATE=1</c>. Without the variable it still
    /// asserts the one thing that must always hold: the committed snapshots are present, so a
    /// deleted Golden directory fails here rather than turning the whole suite into a no-op.
    /// </summary>
    [Fact]
    public void GoldenSnapshots_ArePresent_AndCanBeReRecorded()
    {
        var directory = GoldenDirectory();
        var expectedCount = Rendered().Count;

        if (string.Equals(Environment.GetEnvironmentVariable("EMAIL_GOLDEN_UPDATE"), "1", StringComparison.Ordinal))
        {
            Directory.CreateDirectory(directory);
            foreach (var (name, rendered) in Rendered())
            {
                File.WriteAllText(Path.Combine(directory, name + ".txt"), Tokenise(rendered) + "\n", new UTF8Encoding(false));
            }
        }

        Directory.EnumerateFiles(directory, "*.txt").Should().HaveCount(expectedCount);
    }
}
