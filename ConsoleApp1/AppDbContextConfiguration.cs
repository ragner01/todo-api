using Microsoft.EntityFrameworkCore;
using WebApi.Models;

namespace WebApi.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<TodoItem> Todos => Set<TodoItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // TodoItem mapping
        modelBuilder.Entity<TodoItem>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Title).IsRequired().HasMaxLength(200);
            b.Property(x => x.Description).HasMaxLength(2000);
            b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
            b.Property(x => x.Priority).HasDefaultValue(TodoPriority.Medium);
            b.Property(x => x.LabelsCsv).HasMaxLength(2000);
            b.HasQueryFilter(x => !x.IsDeleted);
            b.HasIndex(x => new { x.IsCompleted, x.IsDeleted, x.DueAtUtc });
        });
    }
}
