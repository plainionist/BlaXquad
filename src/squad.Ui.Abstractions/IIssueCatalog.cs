namespace squad.Ui.Abstractions;

/// <summary>
/// Discovers and parses the fixed workspace issue directory into a deterministically ordered, read-only catalog.
/// Enumeration is re-run on every call so a caller observes edits made during a running session. A missing or
/// empty directory is a successful empty catalog; only a genuine directory or file I/O failure is an exception.
/// </summary>
public interface IIssueCatalog
{
    Task<IReadOnlyList<IssueDescriptor>> ListIssuesAsync(CancellationToken cancellationToken = default);
}
