using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

public class OrderPayment : Entity
{
    public Guid OrderId { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; }

    // Transaction details
    public string? TransactionId { get; set; }

    // ISO-4217, lower-case as Stripe returns it. Null on every tender created before online
    // payments existed, and on cash — the till has no currency of its own.
    //
    // Deliberately NOT surfaced on OrderPaymentDto in this slice: that DTO is mirrored by
    // printer-app/Models and pinned by printer-feed.golden.json, so exposing it churns a snapshot
    // and a second repo for a field nothing renders yet. Add it there when a surface needs it.
    public string? Currency { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateTime PaymentDate { get; set; }

    // Additional payment info
    public string? CardLastFourDigits { get; set; }
    public string? CardType { get; set; } // Visa, MasterCard, etc.
    public string? PaymentGateway { get; set; } // Stripe, PayPal, etc.
    public string? PaymentNotes { get; set; }

    // Client operation key for idempotent tender recording (C04 / #523): the POS mints one
    // Guid per cashier action and replays it on retry, so a network timeout after commit
    // cannot bank a second tender. Tenant DBs are per-tenant, so the filtered unique index
    // on this column is tenant-scoped by construction. Null on every tender recorded before
    // it existed and by the table-bill flow, whose idempotency is a deliberate follow-up.
    public Guid? OperationId { get; set; }

    /// <summary>Bill-level retry operation that allocated this tender; null for ordinary till payments.</summary>
    public Guid? TableBillPaymentOperationId { get; set; }
    public virtual TableBillPaymentOperation? TableBillPaymentOperation { get; set; }

    // For refunds
    public bool IsRefunded { get; set; }
    public decimal? RefundedAmount { get; set; }
    public DateTime? RefundDate { get; set; }
    public string? RefundReason { get; set; }

    // Navigation property
    public virtual Order Order { get; set; } = null!;
}
