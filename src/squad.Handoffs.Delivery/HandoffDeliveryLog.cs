namespace squad.Handoffs.Delivery;

/// <summary>Appends timestamped handoff-delivery diagnostics to one fixed log file, creating its directory as needed.</summary>
public sealed class HandoffDeliveryLog
{
    private readonly string myPath;

    public HandoffDeliveryLog(string path)
    {
        myPath = path;
    }

    public void Append(string[] parts)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(myPath)!);
        File.AppendAllText(myPath, $"{Timestamps.Now()} {string.Join(" ", parts)}\n");
    }
}
