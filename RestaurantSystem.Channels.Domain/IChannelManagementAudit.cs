namespace RestaurantSystem.Channels.Domain;

public sealed record ChannelManagementAuditRecord(long Sequence, Guid ActorId, string Action, string ResultCode,
    Guid? OperationId, DateTimeOffset OccurredAt);

public interface IChannelManagementAudit
{
    Task Record(AvailabilityBinding binding, Guid actorId, string action, string resultCode,
        Guid? operationId, DateTimeOffset occurredAt, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChannelManagementAuditRecord>> Read(AvailabilityBinding binding, long? beforeSequence,
        int limit, CancellationToken cancellationToken);
}

public sealed record ChannelManagementConnectionState(bool IsDisconnected, Guid? ActorId, DateTimeOffset? UpdatedAt);

public interface IChannelManagementConnectionState
{
    Task<ChannelManagementConnectionState> Read(AvailabilityBinding binding, CancellationToken cancellationToken);
    Task<ChannelManagementConnectionState> Set(AvailabilityBinding binding, bool isDisconnected, Guid actorId,
        DateTimeOffset now, CancellationToken cancellationToken);
}
