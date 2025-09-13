# Todo Minimal API (ConsoleApp1)

[![.NET CI](https://github.com/ragner01/todo-api/actions/workflows/dotnet.yml/badge.svg)](https://github.com/ragner01/todo-api/actions/workflows/dotnet.yml)

A minimal ASP.NET Core 8 Web API for managing todos using EF Core (SQLite). It includes paging/filtering/sorting, validation, ETags for concurrency, JSON Patch, JWT-protected write operations, rate limiting, labels/priority, soft delete, background reminders, Swagger, Serilog logging, health checks, and OpenTelemetry instrumentation.

## Features
- Minimal APIs with DTOs and validation
- EF Core + SQLite with migrations and seed data
- ETags: 304 on GET with `If-None-Match`; `If-Match` required for PUT/PATCH/DELETE
- JSON Patch support (`application/json-patch+json`)
- Labels and priority filtering; soft delete
- JWT auth: writes require a Bearer token (Swagger supports Authorize)
- Fixed-window rate limiting for the API group
- Background reminder service (configurable interval)
- Health checks (`/health/live`, `/health/ready`)
- Swagger UI at `/swagger`
- Serilog request logging; OpenTelemetry traces/metrics (console exporter)

## Requirements
- .NET 8 SDK

## Getting Started
```bash
# From repo root
dotnet restore
# Run the web app
dotnet run --project ConsoleApp1
# Or
cd ConsoleApp1
dotnet run
```
Swagger UI: http://localhost:5000/swagger (or the port shown in console)

Note on Formatting
- Pull requests targeting `main` are automatically formatted by a workflow (dotnet-format) and changes are pushed back to the PR branch.
- You can also run `dotnet tool install -g dotnet-format` and `dotnet format` locally before committing.

## Configuration
`ConsoleApp1/appsettings.json` keys:
- `ConnectionStrings:Default`: SQLite connection string (default: `Data Source=app.db`).
- `Cors:AllowedOrigins`: Array of allowed origins for CORS.
- `Jwt:Authority`, `Jwt:Audience`: Configure JWT validation (writes require auth).
- `TodoDueReminder:IntervalMinutes`: Background reminder interval.
- `Service:Name`: Name used in OpenTelemetry resources.
- `Serilog`: Logging levels and sinks.

## Database & Migrations
The app applies migrations at startup in Development. To manage migrations manually, see `ConsoleApp1/MIGRATIONS.md`. Quick commands:
```bash
# Install EF CLI if needed
dotnet tool update -g dotnet-ef
# Create initial migration
cd ConsoleApp1
dotnet ef migrations add InitialCreate
# Add labels/priority/soft-delete changes
dotnet ef migrations add AddPriorityLabelsSoftDelete
# Apply
dotnet ef database update
```

## API Overview
- `GET /api/todos` — list with paging/filtering/sorting
  - Query: `page`, `pageSize`, `search`, `sortBy`, `sortDir`, `isCompleted`, `label`, `priority`
- `GET /api/todos/{id}` — get by id, returns `ETag` header; supports `If-None-Match`
- `POST /api/todos` — create (requires auth)
- `PUT /api/todos/{id}` — replace (requires auth, `If-Match`)
- `PATCH /api/todos/{id}` — JSON Patch (requires auth, `If-Match`)
- `PATCH /api/todos/{id}/complete` — toggle complete (requires auth, `If-Match`)
- `DELETE /api/todos/{id}` — soft delete (requires auth, `If-Match`)
- `GET /health/live`, `GET /health/ready` — health endpoints

### ETag Usage
1) Read current ETag:
```bash
curl -i http://localhost:5000/api/todos/{id}
```

## Docker
Build and run locally:
```bash
docker build -t todo-api .
docker run --rm -p 8080:8080 todo-api
```
Then open http://localhost:8080/swagger

### GitHub Container Registry (GHCR)
Images are published automatically on pushes to `main` and tags (`v*.*.*`).

Pull the image:
```bash
docker pull ghcr.io/ragner01/todo-api:latest
# or a specific tag/sha
docker pull ghcr.io/ragner01/todo-api:<tag>
```

Run:
```bash
docker run --rm -p 8080:8080 ghcr.io/ragner01/todo-api:latest
```
2) Update with concurrency check:
```bash
curl -i -X PUT \
  -H "Authorization: Bearer <token>" \
  -H "If-Match: W/\"<ticks>\"" \
  -H "Content-Type: application/json" \
  -d '{"title":"New title","description":"...","isCompleted":false,"dueAtUtc":null,"labels":["home"],"priority":"Medium"}' \
  http://localhost:5000/api/todos/{id}
```
304 is returned for `If-None-Match` on GET when unchanged; 412 is returned for mismatched `If-Match` on write.

### JSON Patch Example
```bash
curl -i -X PATCH \
  -H "Authorization: Bearer <token>" \
  -H "If-Match: W/\"<ticks>\"" \
  -H "Content-Type: application/json-patch+json" \
  -d '[{"op":"replace","path":"/title","value":"Updated"}]' \
  http://localhost:5000/api/todos/{id}
```

## Logging & Telemetry
- Serilog request logging is enabled; customize via `appsettings.json`.
- OpenTelemetry emits traces/metrics to console; wire exporters (e.g., OTLP) as needed.

## Health Checks
- `GET /health/live` — liveness (always healthy)
- `GET /health/ready` — readiness (includes DB check)

## Notes
- Writes are protected by JWT; configure `Jwt:Authority` and `Jwt:Audience` for your provider.
- Seed data is inserted on first run if the Todos table is empty.
- Soft delete is used; queries exclude deleted items by default.

## License
No license specified. Add one if needed.
