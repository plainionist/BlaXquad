namespace squad.Host.Runtime;

/// <summary>Distinguishes normal shutdown after readiness from shutdown that interrupted startup.</summary>
public enum RunResult
{
    StoppedAfterReady,
    ShutdownBeforeReady,
}


