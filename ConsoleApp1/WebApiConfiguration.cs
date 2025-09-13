using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using WebApi.Data;
using WebApi.Endpoints;
using WebApi.Middleware;
using WebApi;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Serilog;

// Build WebApplication
var builder = WebApplication.CreateBuilder(args);

// Serilog (structured logging)
builder.Host.UseSerilog((ctx, services, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.FromLogContext()
       .WriteTo.Console();
});

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Todo API", Version = "v1" });
    // JWT bearer support in Swagger UI
    var securityScheme = new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter 'Bearer {token}'"
    };
    c.AddSecurityDefinition("Bearer", securityScheme);
    c.AddSecurityRequirement(new()
    {
        { securityScheme, Array.Empty<string>() }
    });
});
// JSON Patch support (uses Newtonsoft.Json under the hood)
builder.Services.AddControllers().AddNewtonsoftJson();

// Configure EF Core with SQLite (file-based DB)
var connectionString = builder.Configuration.GetConnectionString("Default") ??
                       "Data Source=app.db";
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite(connectionString);
});

// Health checks (DB + liveness)
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("db")
    .AddCheck("self", () => HealthCheckResult.Healthy("OK"));

// CORS (adjust origins as needed)
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? new[]
{
    "http://localhost:5173", "http://localhost:4200"
};
builder.Services.AddCors(options =>
{
    options.AddPolicy("default", policy =>
    {
        policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithOrigins(allowedOrigins)
            .AllowCredentials();
    });
});

// DI for application services
builder.Services.AddScoped<ITodoService, TodoService>();

// Background service example
builder.Services.AddHostedService<TodoDueReminderService>();
builder.Services.Configure<TodoDueReminderOptions>(builder.Configuration.GetSection("TodoDueReminder"));

// AuthN/AuthZ (JWT) - configure via appsettings: Jwt:Authority, Jwt:Audience
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Jwt:Authority"];
        options.TokenValidationParameters = new()
        {
            ValidateAudience = !string.IsNullOrWhiteSpace(builder.Configuration["Jwt:Audience"]),
            ValidAudience = builder.Configuration["Jwt:Audience"]
        };
        // For local dev without HTTPS authority
        options.RequireHttpsMetadata = false;
    });
builder.Services.AddAuthorization();

// Rate limiting (fixed window)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("fixed", opt =>
    {
        opt.Window = TimeSpan.FromSeconds(10);
        opt.PermitLimit = 100;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 0;
    });
});

// OpenTelemetry tracing and metrics
var serviceName = builder.Configuration["Service:Name"] ?? "TodoApi";
builder.Services.AddOpenTelemetry()
    .ConfigureResource(rb => rb.AddService(serviceName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddConsoleExporter());

// Build app
var app = builder.Build();

// Apply migrations / create database on startup (safe for dev)
if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await DbSeeder.SeedAsync(db);
    }
}

// Middlewares
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseCors("default");

// Correlation ID header handling
app.Use(async (ctx, next) =>
{
    if (!ctx.Request.Headers.TryGetValue("X-Correlation-Id", out var cid) || string.IsNullOrWhiteSpace(cid))
    {
        cid = Guid.NewGuid().ToString("N");
        ctx.Response.Headers["X-Correlation-Id"] = cid.ToString();
    }
    ctx.Items["CorrelationId"] = cid.ToString();
    await next();
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diag, httpContext) =>
    {
        diag.Set("RequestId", httpContext.TraceIdentifier);
        diag.Set("User", httpContext.User?.Identity?.Name ?? "anonymous");
        diag.Set("ClientIP", httpContext.Connection.RemoteIpAddress?.ToString());
        diag.Set("EndpointName", httpContext.GetEndpoint()?.DisplayName);
        if (httpContext.Items.TryGetValue("CorrelationId", out var c) && c is string s)
            diag.Set("CorrelationId", s);
        else if (httpContext.Response.Headers.TryGetValue("X-Correlation-Id", out var h))
            diag.Set("CorrelationId", h.ToString());
    };
});

// Swagger (dev only)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Health endpoints
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
// ... existing code ...
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            duration = report.TotalDuration,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration,
                error = e.Value.Exception?.Message
            })
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
});

// Root
app.MapGet("/", () => Results.Redirect("/swagger"));

// Feature endpoints
var todos = app.MapTodoEndpoints();
// Protect write endpoints implicitly via convention: apply limiter to group
todos.RequireRateLimiting("fixed");

// Map controllers (none currently, but required for JSON Patch formatters)
app.MapControllers();

await app.RunAsync();
