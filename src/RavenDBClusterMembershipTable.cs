using Microsoft.Extensions.Logging;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Client.Exceptions.Database;
using Raven.Client.ServerWide.Operations;
using Raven.Client.ServerWide;
using System.Runtime.CompilerServices;
using Raven.Client.Exceptions;
using Orleans.Runtime;

namespace Orleans.StorageProvider.RavenDB;

/// <summary>
/// Represents the RavenDB implementation of the IMembershipTable interface.
/// Manages membership entries and table versioning for distributed systems.
/// </summary>
public class RavenDbMembershipTable(string serviceId, IDocumentStore documentStore, ILoggerFactory loggerFactory) : IMembershipTable
{
    // Stores the unique service ID for this membership table instance.
    private readonly string _serviceId = serviceId;

    // Reference to the RavenDB document store for database interactions.
    private readonly IDocumentStore documentStore = documentStore;

    // Logger instance for recording diagnostics and errors.
    private readonly ILogger _logger = loggerFactory.CreateLogger<RavenDbMembershipTable>();

    // Holds the current table version, defaulting to version 0 with the current UTC timestamp.
    private TableVersion _tableVersion = new TableVersion(0, DateTime.UtcNow.ToString());

    // Generates a random deployment ID, converted to a string, for uniquely identifying this deployment.
    private string _deploymentId = ((ulong)Random.Shared.NextInt64()).ToString();

    /// <summary>
    /// Initializes the membership table in RavenDB.
    /// </summary>
    /// <param name="tryCreate">
    /// Indicates whether to attempt creating the database if it does not exist.
    /// </param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="Exception">
    /// Throws an exception if the database cannot be created or initialized.
    /// </exception>
    public async Task InitializeMembershipTable(bool tryCreate)
    {
        try
        {
            // Step 1: Check if the specified database exists in the RavenDB server.
            try
            {
                // Attempt to retrieve the database record for the specified database.
                await documentStore.Maintenance.Server.SendAsync(
                    new GetDatabaseRecordOperation(documentStore.Database));
            }
            catch (DatabaseDoesNotExistException)
            {
                // Step 2: Handle the case where the database does not exist.
                if (tryCreate)
                {
                    try
                    {
                        // Attempt to create the database if 'tryCreate' is true.
                        await documentStore.Maintenance.Server.SendAsync(
                            new CreateDatabaseOperation(new DatabaseRecord(documentStore.Database)));
                    }
                    catch (Exception createDbEx)
                    {
                        // Log and rethrow any exceptions that occur during database creation.
                        _logger.LogError(createDbEx, "Error creating database '{DatabaseName}'.", documentStore.Database);
                        throw;
                    }
                }
                else
                {
                    // Log an error and throw an exception if 'tryCreate' is false.
                    _logger.LogError("Database '{DatabaseName}' does not exist and tryCreate is set to false.", documentStore.Database);
                    throw;
                }
            }

            // Step 3: Initialize the table version if it doesn't already exist.
            using var session = documentStore.OpenAsyncSession(); // Open a session for interacting with the database.

            // Create an initial table version with version 0 and a new GUID as the ETag.
            var initialVersion = new TableVersion(0, Guid.NewGuid().ToString());

            // Store the table version in the database with a fixed ID ("TableVersion").
            await session.StoreAsync(initialVersion, "TableVersion");

            // Commit the changes to the database.
            await session.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Log and rethrow any errors encountered during initialization.
            _logger.LogError(ex, "Error initializing RavenDB membership table.");
            throw;
        }
    }

    /// <summary>
    /// Reads a specific membership entry from the database for a given silo address.
    /// </summary>
    /// <param name="siloAddress">The address of the silo whose membership entry is to be read.</param>
    /// <returns>
    /// A <see cref="MembershipTableData"/> object containing the membership entry and table version, 
    /// or null if the entry does not exist.
    /// </returns>
    /// <exception cref="Exception">Logs and rethrows any exceptions encountered during the operation.</exception>
    public async Task<MembershipTableData> ReadRow(SiloAddress siloAddress)
    {
        try
        {
            // Open an asynchronous session with the RavenDB document store.
            using var session = documentStore.OpenAsyncSession();

            // Query the database for a membership entry matching the provided silo address, service ID, and deployment ID.
            var extended = await session.Query<MembershipEntryExtended>()
                .Where(x =>
                    // Match silo address by converting it to a string for consistency.
                    x.Entry.SiloAddress.ToParsableString() == siloAddress.ToParsableString() &&
                    // Match the service ID for filtering the correct service group.
                    x.ServiceId == _serviceId &&
                    // Match the deployment ID for filtering the correct deployment context.
                    x.DeploymentId == _deploymentId)
                .FirstOrDefaultAsync(); // Retrieve the first matching entry or null if no match is found.

            // If no matching entry is found, return null.
            if (extended == null)
            {
                return null;
            }

            // Retrieve the change vector (ETag) for the matched entry to track data consistency.
            var changeVector = session.Advanced.GetChangeVectorFor(extended);

            // Prepare the list of membership entries, adding the found entry along with its change vector.
            var entries = new List<Tuple<MembershipEntry, string>>
        {
            Tuple.Create(extended.Entry, changeVector)
        };

            // Return the membership data including the entry and the current table version.
            return new MembershipTableData(entries, _tableVersion);
        }
        catch (Exception ex)
        {
            // Log the error with details for debugging purposes.
            _logger.LogError(ex, "Error reading membership row for silo {SiloAddress}", siloAddress);

            // Rethrow the exception to allow higher layers to handle it appropriately.
            throw;
        }
    }


    /// <summary>
    /// Reads all membership entries and the table version from the database.
    /// </summary>
    /// <returns>
    /// A <see cref="MembershipTableData"/> object containing the list of membership entries and the table version.
    /// </returns>
    /// <exception cref="Exception">Logs and rethrows exceptions encountered during the read operation.</exception>
    public async Task<MembershipTableData> ReadAll()
    {
        try
        {
            // Open an asynchronous session with the RavenDB document store.
            using var session = documentStore.OpenAsyncSession();

            // Query to retrieve all membership entries matching the current service and deployment IDs.
            var query = session.Query<MembershipEntryExtended>()
                               .Where(entry => entry.ServiceId == _serviceId && entry.DeploymentId == _deploymentId);

            // Prepare a list to hold the retrieved membership entries along with their change vectors (ETags).
            var entries = new List<Tuple<MembershipEntry, string>>();

            // Attempt to load the table version from the database using a fixed ID ("TableVersion").
            var version = await session.LoadAsync<TableVersion>("TableVersion");

            // If no version exists, create a new one with version 0 and a unique identifier.
            if (version == null)
            {
                version = new TableVersion(0, Guid.NewGuid().ToString());
            }

            // Execute the query asynchronously and iterate through the results.
            foreach (var entryEx in await query.ToListAsync())
            {
                // Retrieve the change vector (ETag) for each membership entry to track data consistency.
                var etag = session.Advanced.GetChangeVectorFor(entryEx);

                // Add the membership entry and its ETag as a tuple to the result list.
                entries.Add(Tuple.Create(entryEx.Entry, etag));
            }

            // Return the membership data, including the entries and table version.
            return new MembershipTableData(entries, version);
        }
        catch (Exception ex)
        {
            // Log the error along with the exception details for easier debugging.
            _logger.LogError(ex, "Error reading all membership rows");

            throw;
        }
    }

    /// <summary>
    /// Inserts a new membership entry into the database if it does not already exist.
    /// </summary>
    /// <param name="entry">The membership entry to insert.</param>
    /// <param name="tableVersion">The current table version, which is incremented upon insertion.</param>
    /// <returns>
    /// True if the row is successfully inserted; False if an entry with the same ID already exists.
    /// </returns>
    /// <exception cref="Exception">Logs and rethrows exceptions encountered during insertion.</exception>
    public async Task<bool> InsertRow(MembershipEntry entry, TableVersion tableVersion)
    {
        try
        {
            // Open an asynchronous session with the RavenDB document store.
            using var session = documentStore.OpenAsyncSession();

            // Generate a unique document ID for the membership entry based on silo name, service ID, and deployment ID.
            var id = GetDocumentId(entry.SiloName, _serviceId, _deploymentId);

            // Check if an entry with the given ID already exists in the database.
            var existingEntry = await session.LoadAsync<MembershipEntryExtended>(id);
            if (existingEntry != null)
            {
                // If the entry exists, return false indicating no insertion took place.
                return false;
            }

            // Compute the next version of the table (incremented) for consistency tracking.
            var nextVersion = tableVersion.Next();

            // Store the updated table version in the database with a fixed ID ("TableVersion").
            await session.StoreAsync(nextVersion, "TableVersion");

            // Store the new membership entry in the database with its generated document ID.
            await session.StoreAsync(new MembershipEntryExtended(_serviceId, _deploymentId, entry), id);

            // Commit all the changes to the database in a single transaction.
            await session.SaveChangesAsync();

            // Return true indicating the insertion was successful.
            return true;
        }
        catch (Exception ex)
        {
            // Log the error for debugging purposes, including the silo address related to the failure.
            _logger.LogError(ex, "Error inserting membership row for silo {SiloAddress}", entry.SiloAddress);

            // Rethrow the exception to let the caller handle it further.
            throw;
        }
    }

    /// <summary>
    /// Updates an existing membership entry and increments the table version.
    /// </summary>
    /// <param name="entry">The updated membership entry.</param>
    /// <param name="etag">The ETag (change vector) for optimistic concurrency control.</param>
    /// <param name="tableVersion">The current table version to be incremented.</param>
    /// <returns>
    /// True if the update is successful; false if the entry does not exist.
    /// </returns>
    /// <exception cref="Exception">Throws an exception if an error occurs during the update.</exception>
    public async Task<bool> UpdateRow(MembershipEntry entry, string etag, TableVersion tableVersion)
    {
        // Open an asynchronous session with RavenDB.
        using var session = documentStore.OpenAsyncSession();

        // Generate the unique document ID based on silo name, service ID, and deployment ID.
        var id = GetDocumentId(entry.SiloName, _serviceId, _deploymentId);

        // Attempt to load the existing membership entry from the database.
        var extendedEntry = await session.LoadAsync<MembershipEntryExtended>(id);

        // Return false if the entry does not exist, as we cannot update a non-existent record.
        if (extendedEntry == null)
        {
            return false;
        }

        // Step 1: Validate the ETag (change vector) for optimistic concurrency control.
        var currentEtag = session.Advanced.GetChangeVectorFor(extendedEntry);
        if (etag != currentEtag)
        {
            // Throw a concurrency exception if ETags do not match, indicating a concurrent update.
            throw new ConcurrencyException($"ETag mismatch. Expected: {etag}, Found: {currentEtag}");
        }

        // Step 2: Update the existing entry's details with the new values.
        extendedEntry.Entry.Status = entry.Status;                // Update silo status.
        extendedEntry.Entry.SuspectTimes = entry.SuspectTimes;    // Update suspect times.
        extendedEntry.Entry.StartTime = entry.StartTime;          // Update start time.
        extendedEntry.Entry.IAmAliveTime = entry.IAmAliveTime;    // Update last alive time.

        // Step 3: Increment the table version for consistency tracking.
        var nextVersion = tableVersion.Next();

        // Step 4: Save the updated entry and the new table version as a single transaction.
        await session.StoreAsync(nextVersion, "TableVersion");   // Update table version first.
        await session.SaveChangesAsync();                        // Commit all changes to the database.

        // Return true to indicate a successful update.
        return true;
    }

    /// <summary>
    /// Cleans up defunct silo entries that have not reported alive since the specified date.
    /// </summary>
    /// <param name="beforeDate">
    /// The cutoff date/time; any silo entries with an "IAmAliveTime" earlier than this will be deleted.
    /// </param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="Exception">Logs and rethrows any exceptions encountered during cleanup.</exception>
    public async Task CleanupDefunctSiloEntries(DateTimeOffset beforeDate)
    {
        try
        {
            // Open an asynchronous session with the RavenDB document store.
            using var session = documentStore.OpenAsyncSession();

            // Step 1: Query entries where 'IAmAliveTime' is older than the specified cutoff date
            // and matches the current service ID and deployment ID.
            var entriesToDelete = await session.Query<MembershipEntryExtended>()
                .Where(x =>
                    x.Entry.IAmAliveTime < beforeDate &&       // Entries older than the cutoff date.
                    x.ServiceId == _serviceId &&              // Match the service ID.
                    x.DeploymentId == _deploymentId)          // Match the deployment ID.
                .ToListAsync();                               // Execute the query and fetch the results.

            // Step 2: Delete each defunct entry from the session.
            foreach (var entry in entriesToDelete)
            {
                session.Delete(entry);
            }

            // Step 3: Commit the changes to the database in a single batch transaction.
            await session.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Log the error with details for debugging purposes.
            _logger.LogError(ex, "Error cleaning up membership table for entries before {BeforeDate}.", beforeDate);

            // Rethrow the exception to ensure higher-level error handling.
            throw;
        }
    }

    /// <summary>
    /// Updates the 'IAmAliveTime' for an existing membership entry in RavenDB.
    /// </summary>
    /// <param name="entry">The membership entry containing the updated 'IAmAliveTime'.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="Exception">Logs and rethrows any exceptions encountered during the update.</exception>
    public async Task UpdateIAmAlive(MembershipEntry entry)
    {
        try
        {
            // Step 1: Open an asynchronous session with RavenDB.
            using var session = documentStore.OpenAsyncSession();

            // Step 2: Generate the unique document ID for the entry.
            var id = GetDocumentId(entry.SiloAddress.ToParsableString(), _serviceId, _deploymentId);

            // Step 3: Attempt to load the existing membership entry.
            var extended = await session.LoadAsync<MembershipEntryExtended>(id);

            // Step 4: Handle cases where the entry does not exist.
            if (extended == null)
            {
                _logger.LogWarning("Attempted to update IAmAlive for non-existent silo {SiloAddress}.", entry.SiloAddress);
                return; // Early exit if no entry is found.
            }

            // Step 5: Update only the 'IAmAliveTime' property.
            extended.Entry.IAmAliveTime = entry.IAmAliveTime;

            // Step 6: Attempt to save changes, handling concurrency issues if they occur.
            try
            {
                await session.SaveChangesAsync();
            }
            catch (Raven.Client.Exceptions.ConcurrencyException ex)
            {
                // Step 7: Handle concurrency exceptions with a warning log.
                _logger.LogWarning(ex, "Concurrency exception updating IAmAlive for silo {SiloAddress}. Retrying.", entry.SiloAddress);

                // Optional: Implement retry logic here if needed.
                throw; // Re-throw the exception so higher-level logic (e.g., Orleans) can handle it.
            }
        }
        catch (Exception ex)
        {
            // Step 8: Log unexpected errors and rethrow the exception for further handling.
            _logger.LogError(ex, "Error updating IAmAlive for silo {SiloAddress}.", entry.SiloAddress);
            throw;
        }
    }

    /// <summary>
    /// Deletes all membership table entries for the specified service ID within the current deployment.
    /// </summary>
    /// <param name="serviceId">The ID of the service whose membership entries are to be deleted.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="Exception">Logs and rethrows any exceptions encountered during the operation.</exception>
    public async Task DeleteMembershipTableEntries(string serviceId)
    {
        try
        {
            // Step 1: Open an asynchronous session with RavenDB.
            using var session = documentStore.OpenAsyncSession();

            // Step 2: Query all entries matching the specified service ID and deployment ID.
            var entriesToDelete = await session.Query<MembershipEntryExtended>()
                .Where(x =>
                    x.ServiceId == serviceId &&               // Match the service ID.
                    x.DeploymentId == _deploymentId)         // Match the deployment ID to ensure context isolation.
                .ToListAsync();                              // Fetch the matching entries.

            // Step 3: Delete each entry found in the query result.
            foreach (var entry in entriesToDelete)
            {
                session.Delete(entry); // Mark each entry for deletion in the current session.
            }

            // Step 4: Commit the deletion changes to the database in a single transaction.
            await session.SaveChangesAsync();

            // Step 5: Log the completion of the operation for monitoring purposes.
            _logger.LogInformation("Deleted {Count} membership table entries for service ID {ServiceId}.", entriesToDelete.Count, serviceId);
        }
        catch (Exception ex)
        {
            // Log the error along with the service ID for easier debugging.
            _logger.LogError(ex, "Error deleting membership table entries for service ID {ServiceId}.", serviceId);

            // Rethrow the exception for higher-level handling.
            throw;
        }
    }

    /// <summary>
    /// Generates a unique document ID for storing membership entries in RavenDB.
    /// </summary>
    /// <param name="siloAddress">The address of the silo.</param>
    /// <param name="serviceId">The service ID used for logical grouping.</param>
    /// <param name="deploymentId">The deployment ID used for isolating deployments.</param>
    /// <returns>A unique document ID string for RavenDB.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] // Optimizes performance by inlining the method during JIT compilation.
    private string GetDocumentId(string siloAddress, string serviceId, string deploymentId)
    {
        // Efficiently constructs the document ID string without intermediate allocations.
        return string.Concat("memberships/", siloAddress, "/", serviceId, "/", deploymentId);
    }
}

/// <summary>
/// Provides RavenDB-specific configuration options for membership tables.
/// </summary>
public class RavenDbMembershipTableOptions
{
    /// <summary>
    /// Gets or sets the service ID used for identifying and isolating data across deployments.
    /// </summary>
    public string ServiceId { get; set; }

    /// <summary>
    /// Gets or sets the RavenDB connection string, specifying URLs and authentication details.
    /// </summary>
    public string ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the RavenDB database name where membership data is stored.
    /// </summary>
    public string DatabaseName { get; set; }
}
