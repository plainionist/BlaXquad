using squad.Ui.Abstractions;

namespace squad.Hosting.Abstractions;

/// <summary>Process-time context a loaded <see cref="IHostingFactory"/> uses to build its hosting bundle.
/// Deliberately depends on <see cref="squad.Ui.Abstractions"/> because every concrete hosting adapter already
/// requires these UI contracts; placing them here makes that dependency explicit rather than reaching for it
/// separately from each plug-in.</summary>
public sealed record HostingContext(string WorkingDirectory, ISquadUi Ui, IIssueCatalog IssueCatalog, IWorkspaceTools WorkspaceTools);
