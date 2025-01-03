# RavenDB Orleans Storage Provider Setup

## Introduction

This guide provides a step-by-step approach to configuring and utilizing RavenDB as the storage provider for Orleans. Orleans is a distributed, fault-tolerant, and scalable framework for building high-performance applications. RavenDB serves as a robust and flexible backend, enabling persistence, clustering, and scalability for Orleans-based applications.

By combining Orleans' virtual actor model with RavenDB's powerful distributed data storage capabilities, this setup provides a reliable and resilient system architecture. Whether you're building microservices, real-time processing systems, or large-scale distributed applications, this guide will help you integrate and optimize RavenDB with Orleans effectively.

---

## Prerequisites

1. **.NET SDK**: Install the latest .NET SDK compatible with Orleans.
2. **RavenDB License**: Accept the RavenDB license as shown in the configuration.

---

# Orleans.StorageProvider.RavenDB

This project provides integration for using **RavenDB** as a storage provider and clustering backend for Microsoft Orleans.

## Features
- **Grain Storage**: Uses RavenDB as a durable storage backend for Orleans grains.
- **Clustering**: Supports RavenDB-based clustering for Orleans silos.
- **Streaming Storage**: Compatible with Orleans streaming infrastructure.
- **Logging Support**: Enables logging configuration for troubleshooting and diagnostics.

---

## Installation

1. Add the required NuGet packages:
   ```bash
   dotnet add package Orleans.StorageProvider.RavenDB
   dotnet add package Microsoft.Extensions.Logging.Console
   ```

2. Install RavenDB server if not already available, or configure connection to an existing server.

---

## Usage

### 1. Configure RavenDB Document Store
```csharp
builder.Services.AddRavenDbDocumentStore(options => 
{
    options.DatabaseName = databaseName;
    options.ConnectionString = connectionString;
});
```
- **`DatabaseName`**: The RavenDB database name to connect to.
- **`ConnectionString`**: The connection string to access RavenDB server.

### 2. Configure Orleans with RavenDB Providers
```csharp
builder.Host.UseOrleans(siloBuilder =>
{
    // Add RavenDB as Grain Storage Provider
    siloBuilder.AddRavenDbGrainStorage("Default", options =>
    {
        options.ServiceId = serviceId;
        options.CollectionName = "PlaceholderCollection";
    });

    // Add RavenDB Clustering
    siloBuilder.AddRavenDBClustering(options =>
    {
        options.ServiceId = serviceId;
    });

    // Configure logging
    siloBuilder.ConfigureLogging(logging =>
    {
        logging.SetMinimumLevel(LogLevel.Debug);
        logging.AddConsole(); // Example: Console logging
    });
});
```

### Key Configurations
- **Stream Provider**: Adds in-memory stream provider named `DefaultStreamProvider`.
- **PubSubStore**: Configures memory-based storage for PubSub.
- **Grain Storage**: Configures RavenDB-backed storage for grains, associating it with a specified collection.
- **Clustering**: Enables RavenDB clustering for Orleans silo management.
- **Logging**: Sets the logging level and output to console. Additional providers can be added as needed.

---

## Notes
- Ensure RavenDB server is accessible and configured with required permissions.
- Replace placeholders (`databaseName`, `connectionString`, `serviceId`) with actual values.
- Test connectivity to RavenDB before deploying to production.

---

## Logging Configuration
Example: Adding file logging support:
```csharp
logging.AddFile("Logs/orleans-log.txt");
```

---

## References
- [Microsoft Orleans Documentation](https://learn.microsoft.com/en-us/dotnet/orleans/)
- [RavenDB Documentation](https://ravendb.net/docs/)

---

## License
MIT License


