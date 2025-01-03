using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Orleans.Storage;
using Raven.Client.Documents;
using System.Runtime.CompilerServices;

namespace Orleans.StorageProviders.RavenDB;

/// <summary>
/// Implements RavenDB-based grain storage for Orleans.
/// Provides state persistence and retrieval for grains.
/// </summary>
public class RavenDbGrainStorage : IGrainStorage
{
    private readonly string _serviceId;
    private readonly IDocumentStore _store;
    private readonly string _collectionName;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RavenDbGrainStorage"/> class.
    /// </summary>
    /// <param name="serviceId">The service ID for identifying data isolation.</param>
    /// <param name="collectionName">The RavenDB collection name for storing grain states.</param>
    /// <param name="store">The RavenDB document store instance.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers.</param>
    public RavenDbGrainStorage(string serviceId, string collectionName, IDocumentStore store, ILoggerFactory loggerFactory)
    {
        // Dependency injection setup
        _serviceId = serviceId ?? throw new ArgumentNullException(nameof(serviceId));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _collectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
        _logger = loggerFactory?.CreateLogger<RavenDbGrainStorage>() ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <summary>
    /// Reads the state of a grain from RavenDB.
    /// </summary>
    /// <typeparam name="T">Type of the grain state.</typeparam>
    /// <param name="grainType">Type of the grain.</param>
    /// <param name="grainId">Unique identifier for the grain.</param>
    /// <param name="grainState">The grain state object to populate.</param>
    public async Task ReadStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
    {
        try
        {
            using var session = _store.OpenAsyncSession();
            // Generate document ID based on grain type and ID
            var id = GetDocumentId(grainType, grainId);

            var state = await session.LoadAsync<GrainState<T>>(id);
            if (state != null)
            {
                state.State = grainState.State;
                state.ETag = grainState.ETag;
                state.RecordExists = grainState.RecordExists;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading grain state for {GrainType} {GrainId}", grainType, grainId);
            throw;
        }
    }

    /// <summary>
    /// Writes the state of a grain to RavenDB.
    /// </summary>
    /// <typeparam name="T">Type of the grain state.</typeparam>
    /// <param name="grainType">Type of the grain.</param>
    /// <param name="grainId">Unique identifier for the grain.</param>
    /// <param name="grainState">The grain state object to persist.</param>
    public async Task WriteStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
    {
        using var session = _store.OpenAsyncSession();

        var id = GetDocumentId(grainType, grainId);

        var state = new GrainState<T> { State = grainState.State, ETag = grainState.ETag, RecordExists = true };

        try
        {
            if (grainState.ETag == null)
            {
                await session.StoreAsync(state, id);
            }

            // Save changes and update the ETag
            await session.SaveChangesAsync();        
        }
        catch (Raven.Client.Exceptions.ConcurrencyException e)
        {
            // Handle concurrency issues and throw an Orleans-specific exception
            throw new InconsistentStateException("ETag mismatch", grainState.ETag, e);
        }
    }

    /// <summary>
    /// Clears the state of a grain in RavenDB.
    /// </summary>
    /// <typeparam name="T">Type of the grain state.</typeparam>
    /// <param name="grainType">Type of the grain.</param>
    /// <param name="grainId">Unique identifier for the grain.</param>
    /// <param name="grainState">The grain state object to clear.</param>
    public async Task ClearStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
    {
        try
        {
            using var session = _store.OpenAsyncSession();
            var id = GetDocumentId(grainType, grainId);

            // Load and delete the document if it exists
            var storedState = await session.LoadAsync<GrainState<T>>(id);
            if (storedState != null)
            {
                session.Delete(storedState);
                await session.SaveChangesAsync();
            }

            // Clear the ETag to indicate no state exists
            grainState.ETag = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing grain state for {GrainType} {GrainId}", grainType, grainId);
            throw;
        }
    }

    /// <summary>
    /// Generates a unique document ID for storing grain state in RavenDB.
    /// </summary>
    /// <param name="grainType">Type of the grain.</param>
    /// <param name="grainId">Unique identifier for the grain.</param>
    /// <returns>A unique document ID string.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string GetDocumentId(string grainType, GrainId grainId)
    {
        return $"{_serviceId}/{_collectionName}/{grainType}/{grainId}";
    }
}
