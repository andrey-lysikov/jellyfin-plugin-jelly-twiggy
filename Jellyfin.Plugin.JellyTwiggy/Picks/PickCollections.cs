using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.JellyTwiggy.Picks;

/// <summary>The two collection slots managed by the plugin.</summary>
public enum PickCollectionKind
{
    /// <summary>The holiday or franchise collection.</summary>
    Themed,

    /// <summary>The rotating server-wide top.</summary>
    Best
}

/// <summary>Creates, renames and synchronizes the collections that contain the current picks.</summary>
public class PickCollections
{
    private readonly ILibraryManager _libraryManager;
    private readonly ICollectionManager _collectionManager;
    private readonly IServerConfigurationManager _serverConfiguration;

    /// <summary>Initializes a new instance of the <see cref="PickCollections"/> class.</summary>
    public PickCollections(
        ILibraryManager libraryManager,
        ICollectionManager collectionManager,
        IServerConfigurationManager serverConfiguration)
    {
        _libraryManager = libraryManager;
        _collectionManager = collectionManager;
        _serverConfiguration = serverConfiguration;
    }

    /// <summary>Gets the collection's stable default name in the server language.</summary>
    public string DefaultName(PickCollectionKind kind) => PickNames.Get(
        kind == PickCollectionKind.Themed ? "collectionThemed" : "collectionBest",
        _serverConfiguration.Configuration.UICulture);

    /// <summary>Gets the collection's current name.</summary>
    public string Name(PickCollectionKind kind, PickResult? result) =>
        PickCollectionRules.Title(DefaultName(kind), result);

    /// <summary>Synchronizes one collection and returns its id; an empty result deletes an existing managed collection.</summary>
    public async Task<Guid?> SyncAsync(
        PickCollectionKind kind,
        PickResult? result,
        Guid? collectionId,
        CancellationToken cancellationToken)
    {
        var itemIds = result?.AllItemIds()
            .Distinct()
            .Where(id => _libraryManager.GetItemById(id) is { IsFolder: false })
            .ToList() ?? [];
        var collection = collectionId.HasValue
            ? _libraryManager.GetItemById<BoxSet>(collectionId.Value)
            : null;

        if (itemIds.Count == 0)
        {
            if (collection is not null)
            {
                _libraryManager.DeleteItem(collection, new DeleteOptions { DeleteFileLocation = false });
            }

            return null;
        }

        if (collection is null)
        {
            var created = await _collectionManager.CreateCollectionAsync(new CollectionCreationOptions
            {
                Name = Name(kind, result),
                IsLocked = true,
                ItemIdList = itemIds.Select(id => id.ToString("N")).ToList()
            }).ConfigureAwait(false);
            return created.Id;
        }

        var name = Name(kind, result);
        if (!string.Equals(collection.Name, name, StringComparison.Ordinal))
        {
            collection.Name = name;
            await collection.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
        }

        var wanted = itemIds.ToHashSet();
        var current = collection.LinkedChildren
            .Select(child => child.ItemId)
            .OfType<Guid>()
            .ToHashSet();
        var removed = current.Where(id => !wanted.Contains(id)).ToList();
        var added = itemIds.Where(id => !current.Contains(id)).ToList();
        if (removed.Count > 0)
        {
            await _collectionManager.RemoveFromCollectionAsync(collection.Id, removed).ConfigureAwait(false);
        }

        if (added.Count > 0)
        {
            await _collectionManager.AddToCollectionAsync(collection.Id, added).ConfigureAwait(false);
        }

        return collection.Id;
    }
}
