using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyTwiggy.Picks;

/// <summary>Brings the channels up to date after start and after the admin changes the plugin settings.</summary>
public sealed class PicksStartup : IHostedService, IDisposable
{
    // Give the server time to load libraries and channels before the first pick.
    private static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(1);

    private readonly PickService _service;
    private readonly ILogger<PicksStartup> _logger;
    private readonly CancellationTokenSource _stopping = new();

    /// <summary>Initializes a new instance of the <see cref="PicksStartup"/> class.</summary>
    public PicksStartup(PickService service, ILogger<PicksStartup> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Plugin.Instance is { } plugin)
        {
            plugin.ConfigurationChanged += OnConfigurationChanged;
        }

        _ = RunAsync(async token =>
        {
            await Task.Delay(StartDelay, token).ConfigureAwait(false);
            await _service.EnsureFreshAsync(token).ConfigureAwait(false);
        });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (Plugin.Instance is { } plugin)
        {
            plugin.ConfigurationChanged -= OnConfigurationChanged;
        }

        _stopping.Cancel();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose() => _stopping.Dispose();

    private void OnConfigurationChanged(object? sender, BasePluginConfiguration configuration)
    {
        _ = RunAsync(_service.RebuildAsync);
    }

    private async Task RunAsync(Func<CancellationToken, Task> work)
    {
        try
        {
            await work(_stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Jelly Twiggy: failed to update the picks");
        }
    }
}
