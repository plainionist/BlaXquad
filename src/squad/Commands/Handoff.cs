using squad.Process;
using squad.Configuration;
using squad.Domain;
using squad.Handoffs;

namespace squad.Commands;

static class Handoff
{
    const string CommitUsage =
        "Usage: handoff commit --to <role>[,<role>...] --task <short-stable-task-name> [--commit <revision>] [--priority NN]";
    const string NoteUsage =
        "Usage: handoff note --to <role>[,<role>...] --message <one line, max 80 chars> [--priority NN]";

    static readonly string UsageText = $"""
        Usage:
          {CommitUsage}
          {NoteUsage}

        Defaults: the sender is inferred from the current role worktree; --commit defaults to HEAD;
        --priority defaults to 50. A default HEAD handoff requires a clean worktree.
        """.ReplaceLineEndings("\n");

    static readonly HashSet<string> CommitOptions = ["--to", "--task", "--commit", "--priority"];
    static readonly HashSet<string> NoteOptions = ["--to", "--message", "--priority"];

    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(UsageText);
            return 0;
        }

        var intent = args[0];
        var kind = ParseKind(intent);
        if (kind is null)
        {
            Console.Error.WriteLine($"Unknown handoff command '{intent}'.");
            Console.Error.WriteLine();
            Console.Error.WriteLine(UsageText);
            return 1;
        }

        var rest = args[1..];
        if (rest.Length > 0 && rest[0] is "-h" or "--help")
        {
            Console.WriteLine(kind == HandoffKind.GitHandoff ? CommitUsage : NoteUsage);
            return 0;
        }

        try
        {
            var roleWorktreeRoot = Path.GetFullPath(ProjectRoot.ResolveViaGit());
            var projectRoot = ProjectRoot.ResolveProjectRoot(roleWorktreeRoot);
            var members = SquadConfig.ReadMembers(projectRoot);
            var sender = CurrentRoleResolver.Resolve(members, roleWorktreeRoot).Id.Value;

            if (!SquadConfig.MemberKnown(members, sender))
            {
                Console.Error.WriteLine($"Unknown sender role: {sender}");
                return 1;
            }

            return kind == HandoffKind.GitHandoff
                ? RunCommit(rest, roleWorktreeRoot, members, sender)
                : RunNote(rest, roleWorktreeRoot, members, sender);
        }
        catch (CliExitException ex)
        {
            if (!string.IsNullOrEmpty(ex.Message))
            {
                Console.Error.WriteLine(ex.Message);
            }
            return ex.ExitCode;
        }
    }

    /// <summary>Maps the first CLI token to its <see cref="HandoffKind"/> once, so every later branch (help
    /// selection, dispatch) reads the enum instead of re-comparing the raw token.</summary>
    static HandoffKind? ParseKind(string intent) => intent switch
    {
        "commit" => HandoffKind.GitHandoff,
        "note" => HandoffKind.Note,
        _ => null,
    };

    static int RunCommit(string[] args, string roleWorktreeRoot, IReadOnlyList<SquadConfigMember> members, string sender)
    {
        var (options, errors) = ParseOptions(args, "commit", CommitOptions);
        options.TryGetValue("--to", out var to);
        options.TryGetValue("--task", out var task);
        var explicitRevision = options.TryGetValue("--commit", out var revision);
        var priority = options.GetValueOrDefault("--priority", "50");

        var (recipients, recipientErrors) = ValidateRecipients(to, members);
        errors.AddRange(recipientErrors);

        if (string.IsNullOrWhiteSpace(to))
        {
            errors.Add("Missing required option '--to'.");
        }
        if (string.IsNullOrWhiteSpace(task))
        {
            errors.Add("Missing required option '--task'.");
        }
        else if (task.Length > 80)
        {
            errors.Add($"Option '--task' must be no longer than 80 characters; got {task.Length}.");
        }
        if (!HandoffPriority.IsValid(priority))
        {
            errors.Add($"Option '--priority' must be two digits from 00 to 99; got '{priority}'.");
        }

        string? canonicalCommit = null;
        if (errors.Count == 0)
        {
            if (!explicitRevision)
            {
                var dirtyError = CheckClean(roleWorktreeRoot);
                if (dirtyError is not null)
                {
                    errors.Add(dirtyError);
                }
            }

            if (errors.Count == 0)
            {
                var (canonical, commitError) = ResolveCommit(revision ?? "HEAD", explicitRevision, roleWorktreeRoot);
                if (commitError is not null)
                {
                    errors.Add(commitError);
                }
                else
                {
                    canonicalCommit = canonical;
                }
            }
        }

        if (errors.Count > 0)
        {
            return ReportErrors(errors, CommitUsage);
        }

        var stateDir = HandoffQueue.Root(roleWorktreeRoot);
        var outboxFile = WriteHandoff(stateDir, HandoffKind.GitHandoff, priority, recipients, sender, task: task, commit: canonicalCommit, message: null);
        Console.WriteLine($"HANDOFF QUEUED: {outboxFile}");
        return 0;
    }

    static int RunNote(string[] args, string roleWorktreeRoot, IReadOnlyList<SquadConfigMember> members, string sender)
    {
        var (options, errors) = ParseOptions(args, "note", NoteOptions);
        options.TryGetValue("--to", out var to);
        options.TryGetValue("--message", out var message);
        var priority = options.GetValueOrDefault("--priority", "50");

        var (recipients, recipientErrors) = ValidateRecipients(to, members);
        errors.AddRange(recipientErrors);

        if (string.IsNullOrWhiteSpace(to))
        {
            errors.Add("Missing required option '--to'.");
        }
        if (string.IsNullOrWhiteSpace(message))
        {
            errors.Add("Missing required option '--message'.");
        }
        else if (message.Length > 80)
        {
            errors.Add($"Option '--message' must be no longer than 80 characters; got {message.Length}.");
        }
        if (!HandoffPriority.IsValid(priority))
        {
            errors.Add($"Option '--priority' must be two digits from 00 to 99; got '{priority}'.");
        }

        if (errors.Count > 0)
        {
            return ReportErrors(errors, NoteUsage);
        }

        var stateDir = HandoffQueue.Root(roleWorktreeRoot);
        var outboxFile = WriteHandoff(stateDir, HandoffKind.Note, priority, recipients, sender, task: null, commit: null, message: message);
        Console.WriteLine($"HANDOFF QUEUED: {outboxFile}");
        return 0;
    }

    static int ReportErrors(List<string> errors, string usage)
    {
        Console.Error.WriteLine("HANDOFF INVALID");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Errors:");
        foreach (var error in errors)
        {
            Console.Error.WriteLine($"- {error}");
        }
        Console.Error.WriteLine();
        Console.Error.WriteLine(usage);
        return 2;
    }

    /// <summary>Parses <c>--option value</c> pairs, rejecting missing values, duplicates, unknown or
    /// out-of-intent options, and unexpected positional arguments.</summary>
    static (Dictionary<string, string> options, List<string> errors) ParseOptions(string[] args, string intent, IReadOnlySet<string> allowed)
    {
        var options = new Dictionary<string, string>();
        var errors = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                errors.Add($"Unexpected argument '{arg}'.");
                continue;
            }

            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                errors.Add($"Option '{arg}' requires a value.");
                continue;
            }

            var value = args[++i];
            if (!allowed.Contains(arg))
            {
                errors.Add($"Option '{arg}' is not valid for '{intent}'.");
                continue;
            }
            if (options.ContainsKey(arg))
            {
                errors.Add($"Duplicate option '{arg}'.");
                continue;
            }
            options[arg] = value;
        }

        return (options, errors);
    }

    static (List<string> recipients, List<string> errors) ValidateRecipients(string? to, IReadOnlyList<SquadConfigMember> members)
    {
        if (string.IsNullOrWhiteSpace(to))
        {
            return ([], []);
        }

        var recipients = to.Split(',');
        var errors = new List<string>();
        var seen = new HashSet<string>();
        foreach (var recipient in recipients)
        {
            if (string.IsNullOrWhiteSpace(recipient))
            {
                errors.Add("Option '--to' contains an empty recipient.");
            }
            if (recipient.Contains('_'))
            {
                errors.Add($"Recipient role '{recipient}' is invalid; role names may not contain underscores.");
            }
            if (seen.Contains(recipient))
            {
                errors.Add($"Duplicate recipient '{recipient}'.");
            }
            if (!string.IsNullOrWhiteSpace(recipient) && !SquadConfig.MemberKnown(members, recipient))
            {
                errors.Add($"Unknown recipient role '{recipient}'.");
            }
            seen.Add(recipient);
        }
        return (recipients.ToList(), errors);
    }

    /// <summary>Rejects a worktree with staged, unstaged, or untracked non-ignored changes so a default `HEAD`
    /// handoff never silently hands off a stale commit.</summary>
    static string? CheckClean(string workingDir)
    {
        var status = ProcessRunner.Run("git", ["status", "--porcelain"], workingDir);
        if (status.ExitCode == 0 && !string.IsNullOrWhiteSpace(status.StdOut))
        {
            return "Worktree has uncommitted changes; commit them before handing off HEAD, " +
                "or hand off an explicit '--commit' revision.";
        }
        return null;
    }

    /// <summary>Resolves `HEAD` or an explicit Git revision to a commit and returns Git's canonical
    /// ten-character abbreviation.</summary>
    static (string? canonical, string? error) ResolveCommit(string revision, bool explicitRevision, string workingDir)
    {
        var label = explicitRevision ? $"Option '--commit' value '{revision}'" : "HEAD";
        var verify = ProcessRunner.Run("git", ["rev-parse", "--verify", "--quiet", revision + "^{commit}"], workingDir);
        if (verify.ExitCode != 0 || string.IsNullOrWhiteSpace(verify.StdOut))
        {
            return (null, $"{label} must resolve to exactly one Git commit.");
        }

        var sha = verify.StdOut.Trim();
        var canonical = ProcessRunner.Run("git", ["rev-parse", "--short=10", sha], workingDir).StdOut.Trim();
        return (canonical, null);
    }

    static string WriteHandoff(
        string stateDir, HandoffKind kind, string priority, List<string> recipients, string sender,
        string? task, string? commit, string? message)
    {
        var timestampId = Timestamps.IdNow();
        var createdAt = Timestamps.NowOffset();
        var sequence = SequenceCounter.Next(stateDir);
        var id = $"{timestampId}_{sequence}_from_{sender}";
        var recipientSlug = string.Join("_", recipients);
        var filename = $"{priority}_{timestampId}_{sequence}_from_{sender}_to_{recipientSlug}{HandoffDocument.FileSuffix}";

        var outboxDir = HandoffQueue.Outbox(stateDir);
        var outboxFile = Path.Combine(outboxDir, filename);

        var document = new HandoffDocument
        {
            Id = new HandoffId(id),
            From = new SquadMemberId(sender),
            To = recipients.Select(recipient => new SquadMemberId(recipient)).ToList(),
            Recipient = null,
            Priority = HandoffPriority.Parse(priority),
            Kind = kind,
            GitHandoff = kind == HandoffKind.GitHandoff ? new GitHandoffData(task!, new GitCommitId(commit!)) : null,
            Note = kind == HandoffKind.Note ? new NoteData(message!) : null,
            CreatedAt = createdAt,
            EnqueuedAt = null,
            DequeuedAt = null,
            CompletedAt = null,
        };

        Directory.CreateDirectory(outboxDir);
        Directory.CreateDirectory(HandoffQueue.Sent(stateDir));
        Directory.CreateDirectory(HandoffQueue.Failed(stateDir));

        HandoffJson.Write(outboxFile, document);
        return outboxFile;
    }
}
