using Microsoft.EntityFrameworkCore;
using WebApi.Data;
using WebApi.Models;

namespace WebApi;

public interface ITodoService
{
    Task<int> GetOverdueCountAsync(CancellationToken ct = default);
}

public class TodoService(AppDbContext db) : ITodoService
{
    public Task<int> GetOverdueCountAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        return db.Todos.CountAsync(t => !t.IsCompleted && t.DueAtUtc != null && t.DueAtUtc < now, ct);
    }
}
