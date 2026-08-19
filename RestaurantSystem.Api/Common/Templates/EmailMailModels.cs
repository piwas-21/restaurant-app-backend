namespace RestaurantSystem.Api.Common.Templates;

/// <summary>
/// The guest a mail is about — the three fields every operator alert prints in the same card.
/// </summary>
/// <remarks>
/// Grouped because they travel together through four template signatures and two send paths, and
/// because passing them as three loose strings is how a phone number ends up in the email slot:
/// they are all <c>string</c>, so the compiler cannot tell them apart. Backend #355.
/// </remarks>
/// <param name="Name">As the guest typed it; may be empty on a counter or QR order.</param>
/// <param name="Email">The address the mail's guest half was sent to.</param>
/// <param name="Phone">May be empty — a reservation can be taken without one.</param>
public readonly record struct EmailGuest(string Name, string Email, string Phone);

/// <summary>
/// The three addresses an operator mail links to: the API a quick-action button calls, the admin
/// site a dashboard link opens, and the address a reply should go to.
/// </summary>
/// <remarks>
/// Same argument as <see cref="EmailGuest"/> — three interchangeable strings whose order nothing
/// checks. Two of these used to be re-aliased inside every template (<c>var apiBaseUrl = baseUrl;</c>)
/// precisely because the parameter names had stopped saying which was which.
/// </remarks>
/// <param name="ApiBaseUrl">Base URL of this instance's API, e.g. <c>https://demo.example/api</c>'s host.</param>
/// <param name="FrontendBaseUrl">Base URL of the tenant's own site, for the dashboard link.</param>
/// <param name="ContactEmail">The restaurant's contact address, printed in the footer.</param>
public readonly record struct EmailLinks(string ApiBaseUrl, string FrontendBaseUrl, string ContactEmail);

/// <summary>The order an operator alert is about.</summary>
/// <param name="Number">Human order number, e.g. <c>ORD-1</c>.</param>
/// <param name="Type"><c>DineIn</c> / <c>Takeaway</c> / <c>Delivery</c>, as the enum names it.</param>
/// <param name="Total">Order total, in <paramref name="Currency"/>.</param>
/// <param name="Currency">The tenant's currency label — never derived from the mail's language.</param>
/// <param name="Items">Line items, already priced.</param>
/// <param name="QuickActionToken">
/// The order's <c>QuickActionToken</c> — the bearer secret that authorises the anonymous
/// confirm/cancel endpoints (ORDER-TYPE-AVAILABILITY-PLAN §9.20). Null only for orders created
/// before that column existed; their buttons render and then land on "Order Not Found", which is
/// the intended outcome — the owner uses the dashboard link.
/// </param>
/// <param name="SpecialInstructions">Guest's note, or null.</param>
/// <param name="DeliveryAddress">Set for a delivery order, null otherwise.</param>
public sealed record OrderMailDetails(
    string Number,
    string Type,
    decimal Total,
    string Currency,
    IEnumerable<(string name, int quantity, decimal price)> Items,
    string? QuickActionToken = null,
    string? SpecialInstructions = null,
    string? DeliveryAddress = null);

/// <summary>The reservation an operator alert is about.</summary>
/// <param name="Id">Reservation id — the quick-action links carry it.</param>
/// <param name="Date">The calendar day booked, on the RESTAURANT's clock. Never converted (#369).</param>
/// <param name="StartTime">Start of the sitting, wall clock.</param>
/// <param name="EndTime">End of the sitting, wall clock.</param>
/// <param name="NumberOfGuests">Party size.</param>
/// <param name="TableNumber">The table held.</param>
/// <param name="SpecialRequests">Guest's note, or null.</param>
public sealed record ReservationMailDetails(
    Guid Id,
    DateTime Date,
    TimeSpan StartTime,
    TimeSpan EndTime,
    int NumberOfGuests,
    string TableNumber,
    string? SpecialRequests = null);
