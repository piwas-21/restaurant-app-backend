namespace RestaurantSystem.Channels.Domain;

public enum InboxWriteResult { Stored, Duplicate, Conflict }

public interface IWebhookInbox
{
    Task<InboxWriteResult> Receive(WebhookReceipt receipt, CancellationToken cancellationToken);
}
