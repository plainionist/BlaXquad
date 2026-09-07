using System.Reflection;
using squad.AgentProvider.Abstractions;
using squad.Process;

namespace squadHQ.Commands;

/// <summary>Loads the <see cref="IAgentProviderFactory"/> selected by a <see cref="ProviderDescriptor"/> into
/// its own assembly load context, kept alive for the remainder of the process.</summary>
static class ProviderLoader
{
    // Keeps every provider load context reachable so it (and the assemblies it loaded) survive for the process lifetime.
    private static readonly List<ProviderLoadContext> myLoadContexts = [];

    public static IAgentProviderFactory Load(ProviderDescriptor descriptor)
    {
        if (!File.Exists(descriptor.AssemblyPath))
        {
            throw new CliExitException(1, $"Provider assembly not found: '{descriptor.AssemblyPath}'.");
        }

        Assembly assembly;
        try
        {
            var context = new ProviderLoadContext(descriptor.AssemblyPath);
            myLoadContexts.Add(context);
            assembly = context.LoadFromAssemblyPath(descriptor.AssemblyPath);
        }
        catch (Exception exception) when (exception is BadImageFormatException or IOException or UnauthorizedAccessException)
        {
            throw new CliExitException(1, $"Provider assembly could not be loaded: '{descriptor.AssemblyPath}'.", exception);
        }

        var type = assembly.GetType(descriptor.TypeName, throwOnError: false);
        if (type is null || !type.IsPublic || !type.IsClass || type.IsAbstract || !typeof(IAgentProviderFactory).IsAssignableFrom(type))
        {
            throw new CliExitException(1,
                $"Provider type '{descriptor.TypeName}' was not found in '{descriptor.AssemblyPath}', or does not " +
                $"publicly implement {nameof(IAgentProviderFactory)}.");
        }

        var constructor = type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, binder: null, Type.EmptyTypes, modifiers: null);
        if (constructor is null)
        {
            throw new CliExitException(1, $"Provider type '{descriptor.TypeName}' has no public parameterless constructor.");
        }

        try
        {
            return (IAgentProviderFactory)constructor.Invoke(null);
        }
        catch (TargetInvocationException exception)
        {
            throw new CliExitException(1,
                $"Provider type '{descriptor.TypeName}' threw during construction.",
                exception.InnerException ?? exception);
        }
    }
}
