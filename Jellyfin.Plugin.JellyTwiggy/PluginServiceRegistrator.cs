using Jellyfin.Plugin.JellyTwiggy.History;
using Jellyfin.Plugin.JellyTwiggy.Media;
using Jellyfin.Plugin.JellyTwiggy.Picks;
using Jellyfin.Plugin.JellyTwiggy.Storage;
using Jellyfin.Plugin.JellyTwiggy.WebInjection;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.JellyTwiggy;

/// <summary>Registers the plugin's services with the server's DI container.</summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<UserSettingsStore>();
        serviceCollection.AddSingleton<PickStore>();
        serviceCollection.AddSingleton<PickCopies>();
        serviceCollection.AddSingleton<PickWatchedTransfer>();
        serviceCollection.AddSingleton<PickService>();
        // Jellyfin's channel manager takes channels from DI; scheduled tasks are found on their own.
        serviceCollection.AddSingleton<IChannel, ThemedPickChannel>();
        serviceCollection.AddSingleton<IChannel, BestPickChannel>();
        serviceCollection.AddSingleton<PlaybackHistoryStore>();
        serviceCollection.AddSingleton<MediaBadgeService>();
        serviceCollection.AddSingleton<WatchersService>();
        serviceCollection.AddSingleton<SessionsService>();
        serviceCollection.AddSingleton<Handoff.JellyfinHandoffSessions>();
        serviceCollection.AddSingleton<Handoff.IHandoffSessions>(provider => provider.GetRequiredService<Handoff.JellyfinHandoffSessions>());
        serviceCollection.AddSingleton(provider => new Handoff.HandoffService(provider.GetRequiredService<Handoff.IHandoffSessions>()));
        serviceCollection.AddSingleton<StatisticsService>();
        serviceCollection.AddSingleton<Dashboard.SystemMetricsService>();
        serviceCollection.AddSingleton<Dashboard.LibraryCountsService>();
        serviceCollection.Configure<MvcOptions>(options => options.Filters.Add<ResumeFilter>());
        serviceCollection.AddHostedService<PlaybackTracker>();
        serviceCollection.AddHostedService<FileTransformationRegistrar>();
        serviceCollection.AddHostedService<PicksStartup>();
    }
}
