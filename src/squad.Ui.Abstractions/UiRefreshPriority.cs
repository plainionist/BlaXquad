namespace squad.Ui.Abstractions;

/// <summary>Controls whether snapshot publication may wait for coalescing or must release the pending refresh.</summary>
public enum UiRefreshPriority
{
    /// <summary>Allows the publisher to wait for its normal coalescing interval.</summary>
    Deferred,
    /// <summary>Releases a pending refresh without waiting for the coalescing interval.</summary>
    Immediate,
}


