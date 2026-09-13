---
name: run-dotnet-tests
description: 'Run .NET tests reliably in this repository. Use when executing one scenario, filtered tests, the squad.Specs project, affected regression tests, or the full .NET regression suite.'
---

# Run .NET Tests

Use the repository test wrapper for every .NET test run. Never invoke `dotnet test` directly.

## Choose the Scope

Run the smallest scope that can disprove the change, in this order:

1. The dedicated test or scenario.
2. The relevant test project.
3. The affected regression scope.
4. The full regression suite when the change is cross-cutting or the impact is uncertain.

Combine related filters into one invocation when practical. Every `squad.Specs` invocation rebuilds and stages its
black-box test tools.

## Run Tests

From the repository root, pass normal `dotnet test` arguments to `./run-tests.ps1`:

```powershell
./run-tests.ps1 src/squad.Specs/squad.Specs.csproj --settings src/test.runsettings --filter 'FullyQualifiedName~ScenarioName'
```

```powershell
./run-tests.ps1 src/squad.Specs/squad.Specs.csproj --settings src/test.runsettings
```

```powershell
./run-tests.ps1 squad.slnx --settings src/test.runsettings
```

The wrapper writes all test output to `.tmp-tests/latest-test.log`, overwriting the previous run. It preserves the
`dotnet test` exit code and prints one structured result line.

Do not disable MSBuild node reuse. Do not inspect or poll arbitrary `dotnet`, MSBuild, `testhost`, or descendant worker
processes. Reusable build nodes may remain alive after the test run. The run is complete when `./run-tests.ps1`
returns; trust its exit code and `TEST_RESULT` line.

## Handle the Result

- On `TEST_RESULT status=passed`, continue without reading the log.
- On `TEST_RESULT status=failed`, inspect only enough of `.tmp-tests/latest-test.log` to diagnose the relevant failure.
- Do not dump the complete log into agent context unless narrower searches and excerpts are insufficient.