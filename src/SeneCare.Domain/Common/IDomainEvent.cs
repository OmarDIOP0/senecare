namespace SeneCare.Domain.Common;

/// <summary>Something that happened in the domain; dispatched after the transaction commits.</summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
