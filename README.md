# BlaXquad

An **opinionated** squad of pragmatic software engineering agents, built to complete missions in any codebase.

## Install

Requirements:

- Git
- .NET 10 SDK
- Node.js and npm

Build from this repository:

```sh
./install.sh
```

On Windows PowerShell, use `.\install.ps1` or `install.cmd`.
On Linux, install WebKitGTK 4.1 and libnotify before running the application.

The installer prints the published application directory. Add it to `PATH`.

## Use

In a target Git repository, create a `blaxquad/` directory containing:

- `squad.json` with the roles to run.
- `constitution.prompt` with shared instructions.
- `roles/<role>.prompt` with instructions for each configured role.

At startup, each agent reads the constitution and its role prompt recursively.

Launch the squad from the target repository:

```sh
squad-hq launch
```

By default, launch resets dedicated worktrees to the current `HEAD`. To keep
existing work and handoffs, use:

```sh
squad-hq launch --continue
```

Close the desktop window or run this from the target repository to stop:

```sh
squad-hq shutdown
```

## Configuration

`blaxquad/squad.json` defines the roles, their worktrees, and agent settings:

```json
{
  "roles": [
    {
      "name": "coordinator",
      "worktree": "master",
      "receiveMode": "task",
      "agent": { "permissions": "prompt" }
    },
    {
      "name": "coder",
      "worktree": "coder",
      "receiveMode": "task",
      "agent": { "permissions": "approveAll", "model": "gpt-5", "effort": "high" }
    }
  ]
}
```

Use `master` as the worktree name to run a role in the main repository;
any other name creates a dedicated worktree.

Permissions are `prompt` by default and can be set to `approveAll`.

## Acknowledgements

Heavily inspired by [swarm-forge](https://github.com/unclebob/swarm-forge) by @unclebobmartin.
