using System.Diagnostics;
using System.Text.RegularExpressions;

namespace squad.Specs.Support;

public sealed class ScenarioWorkspace : IDisposable
{
    private static readonly Regex AnsiEscape = new(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);
    private readonly Dictionary<string, object> myValues = new(StringComparer.Ordinal);
    private readonly List<Process> myRunningProcesses = [];

    public ScenarioWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "blaxquad-specs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }
    public string RepositoryRootPath => RepositoryRoot();
    public CommandResult? LastResult { get; private set; }

    public string PathInWorkspace(params string[] parts) =>
        parts.Aggregate(Root, Path.Combine);

    public void Set<T>(string key, T value) where T : notnull => myValues[key] = value;

    public T Get<T>(string key) => (T)myValues[key];

    public void WriteFile(string relativePath, string content)
    {
        var path = PathInWorkspace(relativePath.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var normalized = content.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
        File.WriteAllText(path, normalized);
    }

    public CommandResult RunTool(
        string toolName,
        IReadOnlyList<string>? arguments = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? workingDirectory = null)
    {
        var executable = ResolveTool(toolName);
        return Run(executable, arguments ?? [], environment, workingDirectory);
    }

    public Process StartTool(
        string toolName,
        IReadOnlyList<string>? arguments = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? workingDirectory = null)
    {
        return StartProcess(ResolveTool(toolName), arguments, environment, workingDirectory);
    }

    public Process StartProcess(
        string executable,
        IReadOnlyList<string>? arguments = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? workingDirectory = null)
    {
        var startInfo = CreateStartInfo(executable, environment, workingDirectory);
        if (arguments is not null)
        {
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{executable}'.");
        myRunningProcesses.Add(process);
        return process;
    }

    public void WaitUntil(Func<bool> condition, string description, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            Thread.Sleep(25);
        }

        throw new TimeoutException($"Timed out waiting for {description}.");
    }

    public CommandResult RunGit(params string[] arguments) =>
        Run("git", arguments, workingDirectory: Root);

    public void InitializeGitRepository()
    {
        AssertSuccessful(RunGit("init", "--quiet"));
        AssertSuccessful(RunGit("config", "user.name", "BlaXquad Acceptance"));
        AssertSuccessful(RunGit("config", "user.email", "acceptance@example.invalid"));
        WriteFile("README.md", "# Acceptance fixture\n");
        AssertSuccessful(RunGit("add", "."));
        AssertSuccessful(RunGit("commit", "--quiet", "-m", "Initial fixture"));
    }

    public CommandResult Run(
        string executable,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? workingDirectory = null)
    {
        var startInfo = CreateStartInfo(executable, environment, workingDirectory);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{executable}'.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        LastResult = new CommandResult(
            process.ExitCode,
            Normalize(stdout.GetAwaiter().GetResult()),
            Normalize(stderr.GetAwaiter().GetResult()));
        return LastResult;
    }

    public void Dispose()
    {
        foreach (var process in myRunningProcesses)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            process.WaitForExit();
            process.Dispose();
        }

        if (!Directory.Exists(Root))
            return;

        foreach (var path in EnumeratePaths(Root).OrderByDescending(path => path.Length))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                continue;

            File.SetAttributes(path, FileAttributes.Normal);
            if (Directory.Exists(path))
                Directory.Delete(path);
            else
                File.Delete(path);
        }
        foreach (var path in EnumeratePaths(Root))
            File.SetAttributes(path, FileAttributes.Normal);
        Directory.Delete(Root, recursive: true);
    }

    private static string Normalize(string value) =>
        AnsiEscape.Replace(value.Replace("\r\n", "\n"), "");

    private ProcessStartInfo CreateStartInfo(
        string executable,
        IReadOnlyDictionary<string, string?>? environment,
        string? workingDirectory)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory ?? Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (environment is not null)
        {
            foreach (var (name, value) in environment)
                startInfo.Environment[name] = value;
        }
        return startInfo;
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "squad.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }

    private static string ResolveTool(string toolName)
    {
        if (OperatingSystem.IsWindows())
            toolName += ".exe";
        return Path.Combine(AppContext.BaseDirectory, "squad-tools", toolName);
    }

    private static IEnumerable<string> EnumeratePaths(string directory)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            yield return path;
            if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
            {
                foreach (var child in EnumeratePaths(path))
                    yield return child;
            }
        }
    }

    private static void AssertSuccessful(CommandResult result)
    {
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Command failed:{Environment.NewLine}{result.StdErr}");
    }
}



