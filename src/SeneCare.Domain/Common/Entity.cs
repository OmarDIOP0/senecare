namespace SeneCare.Domain.Common;

/// <summary>Base class for entities identified by a time-ordered GUID (v7, index-friendly).</summary>
public abstract class Entity
{
    protected Entity() => Id = Guid.CreateVersion7();

    protected Entity(Guid id) => Id = id;

    public Guid Id { get; private init; }
}
