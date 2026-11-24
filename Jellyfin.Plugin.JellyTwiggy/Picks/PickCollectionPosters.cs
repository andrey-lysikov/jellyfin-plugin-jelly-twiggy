using System.Security.Cryptography;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyTwiggy.Picks;

/// <summary>Creates and maintains posters for the managed collections.</summary>
public class PickCollectionPosters
{
    private const int PosterWidth = 800;
    private const int PosterHeight = 1200;

    private readonly ILibraryManager _libraryManager;
    private readonly IImageProcessor _imageProcessor;
    private readonly IProviderManager _providerManager;
    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<PickCollectionPosters> _logger;

    /// <summary>Initializes a new instance of the <see cref="PickCollectionPosters"/> class.</summary>
    public PickCollectionPosters(
        ILibraryManager libraryManager,
        IImageProcessor imageProcessor,
        IProviderManager providerManager,
        IApplicationPaths applicationPaths,
        ILogger<PickCollectionPosters> logger)
    {
        _libraryManager = libraryManager;
        _imageProcessor = imageProcessor;
        _providerManager = providerManager;
        _applicationPaths = applicationPaths;
        _logger = logger;
    }

    /// <summary>Updates a collection poster when its contents change, preserving custom artwork.</summary>
    public async Task<PickPosterState?> SyncAsync(
        BoxSet collection,
        IReadOnlyList<Guid> itemIds,
        PickPosterState? previous,
        CancellationToken cancellationToken)
    {
        var signature = PickCollectionPosterRules.Signature(itemIds);
        try
        {
            var image = collection.GetImageInfo(ImageType.Primary, 0);
            if (image is not null)
            {
                if (!image.IsLocalFile)
                {
                    return previous ?? Custom(signature);
                }

                var currentHash = await FileHashAsync(image.Path, cancellationToken).ConfigureAwait(false);
                if (currentHash is not null)
                {
                    if (string.IsNullOrEmpty(previous?.ImageHash))
                    {
                        return Custom(signature);
                    }

                    if (!string.Equals(currentHash, previous.ImageHash, StringComparison.Ordinal)
                        || string.Equals(previous.ContentSignature, signature, StringComparison.Ordinal))
                    {
                        return previous;
                    }
                }
            }

            var paths = itemIds
                .Select(id => _libraryManager.GetItemById(id)?.GetImageInfo(ImageType.Primary, 0))
                .Where(image => image is { IsLocalFile: true } && File.Exists(image.Path))
                .Select(image => image!.Path)
                .Take(4)
                .ToList();
            if (paths.Count == 0)
            {
                return previous;
            }

            var single = paths.Count == 1 || !_imageProcessor.SupportsImageCollageCreation;
            var output = Path.Combine(
                _applicationPaths.TempDirectory,
                "jelly-twiggy",
                Guid.NewGuid().ToString("N") + (single ? Path.GetExtension(paths[0]) : ".jpg"));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                if (single)
                {
                    File.Copy(paths[0], output, overwrite: true);
                }
                else
                {
                    _imageProcessor.CreateImageCollage(new ImageCollageOptions
                    {
                        InputPaths = PickCollectionPosterRules.CollagePaths(paths),
                        OutputPath = output,
                        Width = PosterWidth,
                        Height = PosterHeight
                    }, libraryName: null);
                }

                cancellationToken.ThrowIfCancellationRequested();
                var mimeType = single ? MimeTypes.GetMimeType(output) : "image/jpeg";
                await _providerManager.SaveImage(collection, output, mimeType, ImageType.Primary, 0, false, cancellationToken).ConfigureAwait(false);
                await collection.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, cancellationToken).ConfigureAwait(false);
                var savedHash = await FileHashAsync(collection.GetImageInfo(ImageType.Primary, 0)?.Path, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Jelly Twiggy: updated the poster for {Collection}", collection.Name);
                return new PickPosterState { ContentSignature = signature, ImageHash = savedHash ?? string.Empty };
            }
            finally
            {
                DeleteTemporary(output);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Jelly Twiggy: could not update the poster for {Collection}", collection.Name);
            return previous;
        }
    }

    private static PickPosterState Custom(string signature) => new() { ContentSignature = signature };

    private static async Task<string?> FileHashAsync(string? path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    private void DeleteTemporary(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Jelly Twiggy: could not delete temporary poster {Path}", path);
        }
    }
}
