using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VelavanDemo.Application.Attachments;

namespace VelavanDemo.Infrastructure.Attachments;

public sealed class AttachmentReconciliationHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AttachmentReconciliationOptions _options;
    private readonly ILogger<AttachmentReconciliationHostedService> _logger;

    public AttachmentReconciliationHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<AttachmentReconciliationOptions> options,
        ILogger<AttachmentReconciliationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes));

        if (_options.RunOnStartup)
        {
            await RunReconciliationAsync(stoppingToken);
        }

        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunReconciliationAsync(stoppingToken);
        }
    }

    private async Task RunReconciliationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<AttachmentReconciliationService>();

            await service.ReconcileAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Attachment storage reconciliation failed.");
        }
    }
}
