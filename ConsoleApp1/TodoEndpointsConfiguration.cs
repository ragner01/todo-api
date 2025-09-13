using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Any;
using WebApi.Data;
using WebApi.Models;
using WebApi;

namespace WebApi.Endpoints;

public static class TodoEndpoints
{
    public static RouteGroupBuilder MapTodoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/todos")
            .WithTags("Todos");

        // List with paging/filtering/sorting: /api/todos?page=1&pageSize=10&search=foo&sortBy=dueAtUtc&sortDir=desc&isCompleted=false
        group.MapGet("", async (HttpContext http, AppDbContext db, int page = 1, int pageSize = 20, string? search = null,
                string? sortBy = "createdAtUtc", string? sortDir = "desc", bool? isCompleted = null, string? label = null, string? priority = null, CancellationToken ct = default) =>
            {
                page = Math.Max(1, page);
                pageSize = Math.Clamp(pageSize, 1, 200);

                var query = db.Todos.AsNoTracking().AsQueryable();

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var like = $"%{search.Trim()}%";
                    query = query.Where(t =>
                        EF.Functions.Like(t.Title, like) ||
                        (t.Description != null && EF.Functions.Like(t.Description, like)));
                }

                if (isCompleted is not null)
                {
                    query = query.Where(t => t.IsCompleted == isCompleted);
                }

                if (!string.IsNullOrWhiteSpace(label))
                {
                    var l = label.Trim();
                    query = query.Where(t => t.LabelsCsv != null && EF.Functions.Like(t.LabelsCsv, $"%{l}%"));
                }

                if (!string.IsNullOrWhiteSpace(priority) && Enum.TryParse<TodoPriority>(priority, true, out var pr))
                {
                    query = query.Where(t => t.Priority == pr);
                }

                // Sorting
                query = (sortBy?.ToLowerInvariant(), sortDir?.ToLowerInvariant()) switch
                {
                    ("title", "asc") => query.OrderBy(t => t.Title),
                    ("title", "desc") => query.OrderByDescending(t => t.Title),

                    ("dueatutc", "asc") => query.OrderBy(t => t.DueAtUtc),
                    ("dueatutc", "desc") => query.OrderByDescending(t => t.DueAtUtc),

                    ("priority", "asc") => query.OrderBy(t => t.Priority),
                    ("priority", "desc") => query.OrderByDescending(t => t.Priority),

                    ("createdatutc", "asc") => query.OrderBy(t => t.CreatedAtUtc),
                    ("createdatutc", "desc") => query.OrderByDescending(t => t.CreatedAtUtc),

                    ("updatedatutc", "asc") => query.OrderBy(t => t.UpdatedAtUtc),
                    ("updatedatutc", "desc") => query.OrderByDescending(t => t.UpdatedAtUtc),

                    _ => query.OrderByDescending(t => t.CreatedAtUtc)
                };

                var total = await query.CountAsync(ct);
                var items = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(t => new TodoDto(t.Id, t.Title, t.Description, t.IsCompleted, t.DueAtUtc, t.CreatedAtUtc, t.UpdatedAtUtc, ParseLabels(t.LabelsCsv), t.Priority))
                    .ToListAsync(ct);

                var fullUrl = http.Request.GetDisplayUrl();
                var uri = new Uri(fullUrl);
                string BuildPageLink(int p)
                {
                    var qb = QueryString.Create(new Dictionary<string, string?>
                    {
                        ["page"] = p.ToString(),
                        ["pageSize"] = pageSize.ToString(),
                        ["search"] = search,
                        ["sortBy"] = sortBy,
                        ["sortDir"] = sortDir,
                        ["isCompleted"] = isCompleted?.ToString()?.ToLowerInvariant()
                    });
                    var builder = new UriBuilder(uri) { Query = qb.Value };
                    return builder.Uri.ToString();
                }

                string? next = (page * pageSize < total) ? BuildPageLink(page + 1) : null;
                string? prev = (page > 1) ? BuildPageLink(page - 1) : null;

                return Results.Ok(new PagedResult<TodoDto>(items, total, page, pageSize, next, prev));
            })
            .WithName("ListTodos")
            .Produces<PagedResult<TodoDto>>(StatusCodes.Status200OK)
            .WithSummary("List todos with paging, filtering, sorting")
            .WithDescription("Supports paging, text search, label and priority filter, and sort by title/dueAtUtc/createdAtUtc/updatedAtUtc/priority.");

        // Get by id
        group.MapGet("{id:guid}", async (HttpContext http, AppDbContext db, Guid id, CancellationToken ct) =>
            {
                var entity = await db.Todos.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
                if (entity is null) return Results.NotFound();

                var etag = GenerateEtag(entity);
                if (http.Request.Headers.TryGetValue("If-None-Match", out var inm) && inm.Any(v => string.Equals(v, etag, StringComparison.Ordinal)))
                {
                    return Results.StatusCode(StatusCodes.Status304NotModified);
                }

                http.Response.Headers["ETag"] = etag;
                return Results.Ok(new TodoDto(entity.Id, entity.Title, entity.Description, entity.IsCompleted, entity.DueAtUtc, entity.CreatedAtUtc, entity.UpdatedAtUtc, ParseLabels(entity.LabelsCsv), entity.Priority));
            })
            .WithName("GetTodo")
            .Produces<TodoDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithSummary("Get a todo by id")
            .WithDescription("Returns 304 if If-None-Match matches current ETag.");

        // Create
        var create = group.MapPost("", async (AppDbContext db, TodoCreateDto dto, CancellationToken ct) =>
            {
                Validate(dto);

                var entity = new TodoItem
                {
                    Title = dto.Title.Trim(),
                    Description = dto.Description?.Trim(),
                    IsCompleted = false,
                    DueAtUtc = dto.DueAtUtc?.ToUniversalTime(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    Priority = dto.Priority,
                    LabelsCsv = JoinLabels(dto.Labels)
                };

                await db.Todos.AddAsync(entity, ct);
                await db.SaveChangesAsync(ct);

                var resource = new TodoDto(entity.Id, entity.Title, entity.Description, entity.IsCompleted, entity.DueAtUtc, entity.CreatedAtUtc, entity.UpdatedAtUtc, ParseLabels(entity.LabelsCsv), entity.Priority);
                return Results.Created($"/api/todos/{entity.Id}", resource);
            })
            .WithName("CreateTodo")
            .Produces<TodoDto>(StatusCodes.Status201Created)
            .Produces<ValidationProblemDetails>(StatusCodes.Status400BadRequest)
            .RequireAuthorization()
            .WithSummary("Create a new todo")
            .WithDescription("Validates title length and that due date is not in the past.");
        create.WithOpenApi(op =>
        {
            if (op.RequestBody?.Content is { } content && content.TryGetValue("application/json", out var media))
            {
                media.Example = new OpenApiObject
                {
                    ["title"] = new OpenApiString("Buy groceries"),
                    ["description"] = new OpenApiString("Milk, eggs, bread"),
                    ["dueAtUtc"] = new OpenApiString(DateTimeOffset.UtcNow.AddDays(1).ToString("o")),
                    ["labels"] = new OpenApiArray { new OpenApiString("home"), new OpenApiString("errands") },
                    ["priority"] = new OpenApiString("Medium")
                };
            }
            return op;
        });

        // Update
        group.MapPut("{id:guid}", async (HttpContext http, AppDbContext db, Guid id, TodoUpdateDto dto, CancellationToken ct) =>
            {
                Validate(dto);

                var entity = await db.Todos.FirstOrDefaultAsync(t => t.Id == id, ct);
                if (entity is null) return Results.NotFound();

                var currentEtag = GenerateEtag(entity);
                if (!http.Request.Headers.TryGetValue("If-Match", out var ifMatch) || ifMatch.All(v => v != currentEtag))
                    return Results.StatusCode(StatusCodes.Status412PreconditionFailed);

                entity.Title = dto.Title.Trim();
                entity.Description = dto.Description?.Trim();
                entity.IsCompleted = dto.IsCompleted;
                entity.DueAtUtc = dto.DueAtUtc?.ToUniversalTime();
                entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
                entity.Priority = dto.Priority;
                entity.LabelsCsv = JoinLabels(dto.Labels);

                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            })
            .WithName("UpdateTodo")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ValidationProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        // Patch completion status
        group.MapPatch("{id:guid}/complete", async (HttpContext http, AppDbContext db, Guid id, ToggleCompleteDto dto, CancellationToken ct) =>
            {
                var entity = await db.Todos.FirstOrDefaultAsync(t => t.Id == id, ct);
                if (entity is null) return Results.NotFound();

                var currentEtag = GenerateEtag(entity);
                if (!http.Request.Headers.TryGetValue("If-Match", out var ifMatch) || ifMatch.All(v => v != currentEtag))
                    return Results.StatusCode(StatusCodes.Status412PreconditionFailed);

                entity.IsCompleted = dto.IsCompleted;
                entity.UpdatedAtUtc = DateTimeOffset.UtcNow;

                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            })
            .WithName("ToggleComplete")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        // JSON Patch (application/json-patch+json) for partial updates
        group.MapPatch("{id:guid}", async (HttpContext http, AppDbContext db, Guid id, JsonPatchDocument<TodoUpdateDto> patch, CancellationToken ct) =>
            {
                var entity = await db.Todos.FirstOrDefaultAsync(t => t.Id == id, ct);
                if (entity is null) return Results.NotFound();

                var currentEtag = GenerateEtag(entity);
                if (!http.Request.Headers.TryGetValue("If-Match", out var ifMatch) || ifMatch.All(v => v != currentEtag))
                    return Results.StatusCode(StatusCodes.Status412PreconditionFailed);

                var dto = new TodoUpdateDto(
                    entity.Title,
                    entity.Description,
                    entity.IsCompleted,
                    entity.DueAtUtc,
                    ParseLabels(entity.LabelsCsv),
                    entity.Priority
                );

                patch.ApplyTo(dto);
                Validate(dto);

                entity.Title = dto.Title.Trim();
                entity.Description = dto.Description?.Trim();
                entity.IsCompleted = dto.IsCompleted;
                entity.DueAtUtc = dto.DueAtUtc?.ToUniversalTime();
                entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
                entity.Priority = dto.Priority;
                entity.LabelsCsv = JoinLabels(dto.Labels);

                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            })
            .WithName("PatchTodo")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ValidationProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        // Delete
        group.MapDelete("{id:guid}", async (HttpContext http, AppDbContext db, Guid id, CancellationToken ct) =>
            {
                var entity = await db.Todos.FirstOrDefaultAsync(t => t.Id == id, ct);
                if (entity is null) return Results.NotFound();

                var currentEtag = GenerateEtag(entity);
                if (!http.Request.Headers.TryGetValue("If-Match", out var ifMatch) || ifMatch.All(v => v != currentEtag))
                    return Results.StatusCode(StatusCodes.Status412PreconditionFailed);

                entity.IsDeleted = true;
                entity.DeletedAtUtc = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            })
            .WithName("DeleteTodo")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        return group;
    }

    private static void Validate(TodoCreateDto dto)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(dto.Title))
            errors["title"] = ["Title is required."];

        if (dto.Title?.Length > 200)
            errors["title"] = (errors.TryGetValue("title", out var arr) ? arr : []).Concat(new[] { "Title must be at most 200 characters." }).ToArray();

        if (dto.Description?.Length > 2000)
            errors["description"] = ["Description must be at most 2000 characters."];

        if (dto.DueAtUtc is { } due && due < DateTimeOffset.UtcNow.AddMinutes(-1))
            errors["dueAtUtc"] = ["Due date cannot be in the past."];

        if (dto.Labels?.Count > 50)
            errors["labels"] = ["Too many labels (max 50)."];
        if (dto.Labels is { } labels && labels.Any(l => l.Length > 50))
            errors["labels"] = (errors.TryGetValue("labels", out var arr) ? arr : []).Concat(new[] { "Each label must be at most 50 characters." }).ToArray();

        if (errors.Count > 0) throw new ValidationException(errors);
    }

    private static void Validate(TodoUpdateDto dto)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(dto.Title))
            errors["title"] = ["Title is required."];

        if (dto.Title?.Length > 200)
            errors["title"] = (errors.TryGetValue("title", out var arr) ? arr : []).Concat(new[] { "Title must be at most 200 characters." }).ToArray();

        if (dto.Description?.Length > 2000)
            errors["description"] = ["Description must be at most 2000 characters."];

        if (dto.DueAtUtc is { } due && due < DateTimeOffset.UtcNow.AddMinutes(-1))
            errors["dueAtUtc"] = ["Due date cannot be in the past."];

        if (dto.Labels?.Count > 50)
            errors["labels"] = ["Too many labels (max 50)."];
        if (dto.Labels is { } labels && labels.Any(l => l.Length > 50))
            errors["labels"] = (errors.TryGetValue("labels", out var arr) ? arr : []).Concat(new[] { "Each label must be at most 50 characters." }).ToArray();

        if (errors.Count > 0) throw new ValidationException(errors);
    }

    private static string GenerateEtag(TodoItem t)
    {
        var ticks = (t.UpdatedAtUtc ?? t.CreatedAtUtc).ToUnixTimeMilliseconds();
        return $"W/\"{ticks}\""; // weak etag
    }

    private static IReadOnlyList<string> ParseLabels(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return Array.Empty<string>();
        return csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? JoinLabels(IReadOnlyList<string>? labels)
    {
        if (labels is null || labels.Count == 0) return null;
        var cleaned = labels
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return cleaned.Length == 0 ? null : string.Join(',', cleaned);
    }
}

public record TodoDto(
    Guid Id,
    string Title,
    string? Description,
    bool IsCompleted,
    DateTimeOffset? DueAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    IReadOnlyList<string> Labels,
    TodoPriority Priority);

public record TodoCreateDto(
    string Title,
    string? Description,
    DateTimeOffset? DueAtUtc,
    IReadOnlyList<string> Labels,
    TodoPriority Priority = TodoPriority.Medium);

public record TodoUpdateDto(
    string Title,
    string? Description,
    bool IsCompleted,
    DateTimeOffset? DueAtUtc,
    IReadOnlyList<string> Labels,
    TodoPriority Priority);

public record ToggleCompleteDto(bool IsCompleted);

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize, string? Next = null, string? Prev = null);
