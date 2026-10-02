namespace CallingBell.Domain.Common;

public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
}

/// <summary>
/// Entity carrying the standard audit columns and soft-delete flag required by the database standards.
/// Audit values are populated by the persistence layer, never by handlers.
/// </summary>
public abstract class AuditableEntity : BaseEntity, ISoftDeletable
{
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTimeOffset? ModifiedOn { get; set; }
    public bool IsDeleted { get; set; }
}

public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
}
