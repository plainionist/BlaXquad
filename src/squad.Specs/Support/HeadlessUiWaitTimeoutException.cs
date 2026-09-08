namespace squad.Specs.Support;

/// <summary>
/// Reports a bounded wait failure across workspace, CLI, UI, and lifecycle support for one launched, published
/// squad-hq process. Carries a single pre-formatted diagnostics block - process/command state, captured standard
/// output and standard error, and the most recently observed UI state - so a timeout can be diagnosed without
/// re-running the scenario or inspecting raw protocol traffic by hand.
/// </summary>
public sealed class HeadlessUiWaitTimeoutException : TimeoutException
{
    public HeadlessUiWaitTimeoutException(string description, string diagnostics)
        : base($"Timed out waiting for {description}.\n{diagnostics}")
    {
    }
}
