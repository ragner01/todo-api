namespace WebApi;

public sealed class TodoDueReminderOptions
{
    public int IntervalMinutes { get; set; } = 1;
}

public class TodoDueReminderService(ILogger<TodoDueReminderService> logger, ITodoService service, Microsoft.Extensions.Options.IOptionsMonitor<TodoDueReminderOptions> options)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.CurrentValue.IntervalMinutes <= 0 ? 1 : options.CurrentValue.IntervalMinutes));
    try
    {
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var overdue = await service.GetOverdueCountAsync(stoppingToken);
            if (overdue > 0)
            {
                logger.LogWarning("There are {Overdue} overdue todos.", overdue);
            }
            else
            {
                logger.LogInformation("No overdue todos.");
            }
        }
    }
    catch (OperationCanceledException)
    {
        // normal on shutdown
    }
}
}
