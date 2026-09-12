using squad.Configuration;
using squad.Domain;
using squad.Process;

namespace squad.Commands;

static class DoneWithCurrent
{
    public static int Run(string[] args)
    {
        try
        {
            var roleWorktreeRoot = Path.GetFullPath(ProjectRoot.ResolveViaGit());
            var projectRoot = ProjectRoot.ResolveProjectRoot(roleWorktreeRoot);
            var members = SquadConfig.ReadMembers(projectRoot);
            var member = CurrentRoleResolver.Resolve(members, roleWorktreeRoot);
            var role = member.Id.Value;
            if (member.ReceiveMode is null)
            {
                Console.Error.WriteLine($"Unknown role: {role}");
                return 1;
            }

            return member.ReceiveMode == ReceiveMode.Batch
                ? DoneWithCurrentBatch.Run(args)
                : DoneWithCurrentTask.Run(args);
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
}



