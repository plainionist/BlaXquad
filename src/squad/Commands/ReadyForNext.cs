using squad.Configuration;
using squad.Domain;
using squad.Process;

namespace squad.Commands;

static class ReadyForNext
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

                if (string.IsNullOrEmpty(member.RawReceiveMode))
                {
                    Console.Error.WriteLine($"Unknown role: {role}");
                    return 1;
                }

                Console.Error.WriteLine($"INVALID_RECEIVE_MODE: {member.RawReceiveMode} for role {role}");
                return 2;
            }

            return member.ReceiveMode == ReceiveMode.Batch
                ? ReadyForNextBatch.Run(args)
                : ReadyForNextTask.Run(args);
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
