using System.ComponentModel.DataAnnotations;

namespace WebApi.Models;

public enum TodoPriority
{
    Low = 0,
    Medium = 1,
    High = 2
}

public class TodoItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    public bool IsCompleted { get; set; }

    // Optional due date
    public DateTimeOffset? DueAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }

    // Priority
    public TodoPriority Priority { get; set; } = TodoPriority.Medium;

    // Labels as comma-separated values for simple filtering
    [StringLength(2000)]
    public string? LabelsCsv { get; set; }
}
