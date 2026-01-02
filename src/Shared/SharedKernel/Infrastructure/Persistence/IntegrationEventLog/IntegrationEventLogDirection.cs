namespace NetCommerce.SharedKernel.Infrastructure.Persistence.IntegrationEventLog;

/// <summary>
///     Direction of the integration event in the log.
/// </summary>
public enum IntegrationEventLogDirection
{
    /// <summary>
    ///     Event was published (outgoing from this module).
    /// </summary>
    Published = 1,

    /// <summary>
    ///     Event was received (incoming to this module).
    /// </summary>
    Received = 2
}