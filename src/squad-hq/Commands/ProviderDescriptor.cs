namespace squadHQ.Commands;

/// <summary>Identifies the assembly and public factory type used to load one <see cref="squad.AgentProvider.Abstractions.IAgentProviderFactory"/>.</summary>
internal sealed record ProviderDescriptor(string AssemblyPath, string TypeName);
