namespace squad.Ui.Abstractions;

/// <summary>Identifies how an incremental update mutates authoritative transcript state.</summary>
public enum TranscriptUpdateKind
{
    AppendEntry,
    AppendContent,
    ReplaceEntry,
}


