using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Hosting;
using Orleans.Storage;
using Orleans.StorageProvider.RavenDB.Converters;
using Orleans.StorageProviders.RavenDB;
using Raven.Client.Documents;
using Raven.Client.Json.Serialization.NewtonsoftJson;

namespace Orleans.StorageProvider.RavenDB.Extensions;

/// <summary>
/// Provides extension methods for integrating RavenDB with Orleans.
/// </summary>
public static class RavenDbExtensions
{
    /// <summary>
    /// Registers a RavenDB document store in the service collection.
    /// </summary>
    /// <param name="services">The service collection to add the document store to.</param>
    /// <param name="configureOptions">Action to configure RavenDB options.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddRavenDbDocumentStore(this IServiceCollection services, Action<RavenDbOptions> configureOptions)
    {
        // Register RavenDbOptions configuration.
        services.AddOptions<RavenDbOptions>();
        services.Configure(configureOptions);

        // Register IDocumentStore as a singleton.
        services.AddSingleton<IDocumentStore>(sp =>
        {
            // Retrieve RavenDbOptions from the service provider.
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RavenDbOptions>>().Value;

            // Split the connection string into multiple URLs (if needed).
            var connectionstrings = options.ConnectionString.Split(';').ToArray();

            // Initialize the RavenDB document store.
            var documentStore = new DocumentStore
            {
                Urls = connectionstrings,    // Set database URLs.
                Database = options.DatabaseName // Set database name.
            };

            // Configure custom JSON serialization conventions.
            documentStore.Conventions.Serialization = new NewtonsoftJsonSerializationConventions
            {
                CustomizeJsonSerializer = config =>
                {
                    // Add custom converters for Orleans-specific types.
                    config.Converters.Add(new OrleansConverters.SiloAddressConverter());
                    config.Converters.Add(new OrleansConverters.IPAddressConverter());
                    config.Converters.Add(new OrleansConverters.MembershipEntryConverter());
                }
            };

            // Initialize the document store.
            documentStore.Initialize();

            return documentStore; // Return the configured document store.
        });

        return services; // Return updated service collection.
    }

    /// <summary>
    /// Adds RavenDB grain storage to the Orleans silo.
    /// </summary>
    /// <param name="builder">The Orleans silo builder.</param>
    /// <param name="name">The storage name.</param>
    /// <param name="configureOptions">Action to configure RavenDB grain storage options.</param>
    /// <returns>The updated silo builder.</returns>
    public static ISiloBuilder AddRavenDbGrainStorage(this ISiloBuilder builder, string name, Action<RavenDbGrainStorageOptions> configureOptions)
    {
        return builder.ConfigureServices(services =>
        {
            // Register RavenDbGrainStorageOptions for named configuration.
            services.AddOptions<RavenDbGrainStorageOptions>(name);
            services.Configure(name, configureOptions);

            services.AddKeyedSingleton<IGrainStorage>(name, (sp, key) =>
            {
                // Use IOptionsMonitor instead of IOptionsSnapshot
                var options = sp.GetRequiredService<IOptionsMonitor<RavenDbGrainStorageOptions>>().Get(key.ToString());
                var store = sp.GetRequiredService<IDocumentStore>();
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

                // Create and return the RavenDbGrainStorage instance
                return new RavenDbGrainStorage(options.ServiceId, options.CollectionName, store, loggerFactory);
            });
        });
    }

    /// <summary>
    /// Adds RavenDB clustering support for Orleans.
    /// </summary>
    /// <param name="builder">The Orleans silo builder.</param>
    /// <param name="configureOptions">Action to configure RavenDB clustering options.</param>
    /// <returns>The updated silo builder.</returns>
    public static ISiloBuilder AddRavenDBClustering(this ISiloBuilder builder, Action<RavenDbMembershipTableOptions> configureOptions)
    {
        return builder.ConfigureServices(services =>
        {
            // Register RavenDbMembershipTableOptions configuration.
            services.AddOptions<RavenDbMembershipTableOptions>();
            services.Configure(configureOptions);

            // Register the IMembershipTable implementation.
            services.AddSingleton<IMembershipTable>(sp =>
            {
                // Retrieve options and required services.
                var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RavenDbMembershipTableOptions>>().Value;
                var store = sp.GetRequiredService<IDocumentStore>();
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

                // Create and return the RavenDbMembershipTable instance.
                return new RavenDbMembershipTable(options.ServiceId, store, loggerFactory);
            });
        });
    }
}

/// <summary>
/// Options for configuring RavenDB connection.
/// </summary>
public class RavenDbOptions
{
    public string ConnectionString { get; set; } // RavenDB connection string.
    public string DatabaseName { get; set; }    // Database name.
}

/// <summary>
/// Options for configuring RavenDB grain storage.
/// </summary>
public class RavenDbGrainStorageOptions
{
    public string ServiceId { get; set; }              // Service ID for data isolation.
    public string CollectionName { get; set; } = "GrainStates"; // Default collection name.
}

/// <summary>
/// Options for configuring RavenDB membership table.
/// </summary>
public class RavenDbMembershipTableOptions
{
    public string ServiceId { get; set; } // Service ID for identifying membership entries.
}
