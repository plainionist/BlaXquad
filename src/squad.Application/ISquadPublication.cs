using squad.Ui.Abstractions;

namespace squad.Application;

/// <summary>
/// The process-lifetime port through which one squad generation publishes state and transcript changes. The
/// implementation forwards a publication only while the generation that produced it is still the installed one,
/// so a retired generation can never reach the UI.
/// </summary>
public interface ISquadPublication
{
    void NotifyStateChanged(SquadGenerationId generation, UiRefreshPriority priority);
    void PublishTranscriptUpdate(SquadGenerationId generation, TranscriptUpdate update);
}
