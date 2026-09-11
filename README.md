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

- `squad.json` with the reusable roles and the members to run.
- `constitution.prompt` with shared instructions.
- `roles/<role>.prompt` with instructions for each configured role.

At startup, each agent reads the constitution and its role prompt recursively.

Launch the squad from the target repository:

```sh
squad-hq launch
```

By default, launch resets dedicated worktrees to the current `HEAD` and clears
each role's durable handoff queue. To keep existing worktree changes (but not
queued handoffs, which are always cleared on launch), use:

```sh
squad-hq launch --continue
```

Close the desktop window or run this from the target repository to stop:

```sh
squad-hq shutdown
```

## Configuration

`blaxquad/squad.json` (schema version 2) separates reusable `roles` - each backed by a
`blaxquad/roles/<role>.prompt` file - from uniquely named `members`, the participants Headquarters actually
launches. Each member references exactly one role and owns its own worktree, receive mode, and agent settings; two
members may deliberately reference the same role to run it with independent worktrees and provider sessions:

```json
{
  "schemaVersion": 2,
  "leader": "coordinator",
  "roles": ["coordinator", "coder"],
  "members": [
    {
      "name": "coordinator",
      "role": "coordinator",
      "worktree": "master",
      "receiveMode": "task",
      "agent": { "permissions": "prompt" }
    },
    {
      "name": "coder-a",
      "role": "coder",
      "worktree": "coder-a",
      "receiveMode": "task",
      "agent": { "permissions": "approveAll", "model": "gpt-5", "effort": "high" }
    },
    {
      "name": "coder-b",
      "role": "coder",
      "worktree": "coder-b",
      "receiveMode": "task",
      "agent": { "permissions": "approveAll", "model": "gpt-5", "effort": "high" }
    }
  ]
}
```

`leader` identifies the member the dashboard's issue explorer targets when preparing a prompt to process an
issue. It is optional: if omitted or blank, the first member listed in `members` is used as the leader, so there
is always an authoritative leader. If given explicitly, it must exactly match one configured member's `name` - a
role name alone is not a valid leader when that role is not also a member name.

A legacy (schema version 1) document is rejected with a diagnostic describing the identity-preserving migration:
keep each old entry's `name` as its member name, add that name to `roles`, set the member's `role` to the same
value, and leave `leader` unchanged.

Use `master` as the worktree name to run a member in the main repository;
any other name creates a dedicated worktree.

Permissions are `prompt` by default and can be set to `approveAll`.

## Architecture

See the [current architecture](docs/manual/architecture.md) for C4 diagrams,
runtime flows, state ownership, communication boundaries, and a code map.

## Acknowledgements

Heavily inspired by [swarm-forge](https://github.com/unclebob/swarm-forge) by @unclebobmartin.
