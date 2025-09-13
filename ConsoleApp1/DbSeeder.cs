using Microsoft.EntityFrameworkCore;
using WebApi.Data;
using WebApi.Models;

namespace WebApi;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (await db.Todos.AnyAsync(ct)) return;

        var now = DateTimeOffset.UtcNow;
        var sample = new List<TodoItem>
        {
            new() { Title = "Buy groceries", Description = "Milk, eggs, bread", DueAtUtc = now.AddDays(1), Priority = TodoPriority.Medium, LabelsCsv = "home,errands" },
            new() { Title = "Finish report", Description = "Quarterly financials", DueAtUtc = now.AddDays(2), Priority = TodoPriority.High, LabelsCsv = "work,finance" },
            new() { Title = "Call plumber", Description = "Fix kitchen sink", DueAtUtc = now.AddHours(6), Priority = TodoPriority.Low, LabelsCsv = "home,maintenance" }
        };

        db.Todos.AddRange(sample);
        await db.SaveChangesAsync(ct);
    }
}
