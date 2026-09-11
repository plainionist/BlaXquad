namespace squad.Hosting.Abstractions;

/// <summary>The complete pair of lifecycle resources one hosting plug-in contributes. Loading only an
/// <see cref="IWindowHost"/> is insufficient because the host would still need to know which concrete sleep
/// implementation belongs to the selected window host; every hosting plug-in supplies both explicitly.</summary>
public sealed record HostingRuntime(IWindowHost WindowHost, ISleepInhibitor SleepInhibitor);
