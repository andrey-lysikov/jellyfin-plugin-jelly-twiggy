using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyTwiggy.WebInjection;

/// <summary>Registers the index.html transformation with File Transformation on startup.</summary>
// FT lives in another load context: call it via reflection and build JObject from FT's own type.
public sealed class FileTransformationRegistrar : IHostedService
{
    private const string PluginInterfaceType = "Jellyfin.Plugin.FileTransformation.PluginInterface";

    private static readonly Guid IndexTransformationId = Guid.Parse("3d50c708-57cc-4341-b8eb-a623dc4cec44");

    private readonly ILogger<FileTransformationRegistrar> _logger;

    /// <summary>Initializes a new instance of the <see cref="FileTransformationRegistrar"/> class.</summary>
    public FileTransformationRegistrar(ILogger<FileTransformationRegistrar> logger)
    {
        _logger = logger;
    }

    /// <summary>Gets a value indicating whether the index.html transformation was registered.</summary>
    public static bool IsRegistered { get; private set; }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            Register();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Jelly Twiggy: failed to register the index.html transformation");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Register()
    {
        var assembly = AssemblyLoadContext.All
            .SelectMany(context => context.Assemblies)
            .FirstOrDefault(a => a.FullName?.Contains(".FileTransformation", StringComparison.Ordinal) ?? false);
        if (assembly is null)
        {
            _logger.LogWarning("Jelly Twiggy: File Transformation plugin is not installed, the web client UI is disabled");
            return;
        }

        var registerMethod = assembly.GetType(PluginInterfaceType)?.GetMethod("RegisterTransformation");
        var payloadType = registerMethod?.GetParameters().FirstOrDefault()?.ParameterType;
        var parseMethod = payloadType?.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [typeof(string)]);
        if (registerMethod is null || parseMethod is null)
        {
            _logger.LogWarning("Jelly Twiggy: unsupported File Transformation version, the web client UI is disabled");
            return;
        }

        var json = JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["id"] = IndexTransformationId.ToString(),
            ["fileNamePattern"] = "index.html",
            ["callbackAssembly"] = typeof(IndexHtmlTransformer).Assembly.FullName,
            ["callbackClass"] = typeof(IndexHtmlTransformer).FullName,
            ["callbackMethod"] = nameof(IndexHtmlTransformer.Transform)
        });

        registerMethod.Invoke(null, [parseMethod.Invoke(null, [json])]);
        IsRegistered = true;
        _logger.LogInformation("Jelly Twiggy: registered the index.html transformation");
    }
}
