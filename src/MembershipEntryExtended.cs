
namespace Orleans.StorageProvider.RavenDB;

using Orleans;
using Orleans.Runtime;

/// <summary>
/// Represents an extended version of a membership entry, which includes additional metadata 
/// such as Service ID and Deployment ID. This class wraps around the core MembershipEntry object 
/// to provide more contextual information.
/// </summary>
public class MembershipEntryExtended
{
    /// <summary>
    /// Gets the unique identifier for the service associated with this membership entry.
    /// </summary>
    public string ServiceId { get; }

    /// <summary>
    /// Gets the unique identifier for the deployment associated with this membership entry.
    /// </summary>
    public string DeploymentId { get; }

    /// <summary>
    /// Gets the core membership entry containing details about the silo, such as its address and status.
    /// </summary>
    public MembershipEntry Entry { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MembershipEntryExtended"/> class.
    /// </summary>
    /// <param name="serviceId">The unique identifier for the service.</param>
    /// <param name="deploymentId">The unique identifier for the deployment.</param>
    /// <param name="entry">The core membership entry to wrap.</param>
    public MembershipEntryExtended(string serviceId, string deploymentId, MembershipEntry entry)
    {
        // Assign the provided service ID, deployment ID, and membership entry to the properties.
        ServiceId = serviceId;
        DeploymentId = deploymentId;
        Entry = entry;
    }

    // Convenient accessors to expose relevant properties of the wrapped MembershipEntry object.

    /// <summary>
    /// Gets the address of the silo associated with this membership entry.
    /// </summary>
    public SiloAddress SiloAddress => Entry.SiloAddress;

    /// <summary>
    /// Gets the status of the silo (e.g., Active, Dead, Joining).
    /// </summary>
    public SiloStatus Status => Entry.Status;

    // Additional properties can be added here if more details from MembershipEntry are required.

    /// <summary>
    /// Returns a string representation of the extended membership entry for logging or debugging.
    /// </summary>
    /// <returns>A summary string containing the service ID, deployment ID, and the membership entry.</returns>
    public override string ToString()
    {
        // Produces a brief textual representation of the object.
        return $"ServiceId={ServiceId} DeploymentId={DeploymentId} Entry={Entry}";
    }

    /// <summary>
    /// Returns a detailed string representation of the extended membership entry, including full details 
    /// of the wrapped membership entry.
    /// </summary>
    /// <returns>A detailed string containing all relevant information.</returns>
    public string ToFullString()
    {
        // Uses the ToFullString() method of the inner MembershipEntry to include all nested details.
        return $"ServiceId={ServiceId} DeploymentId={DeploymentId} Entry={Entry.ToFullString()}";
    }
}
