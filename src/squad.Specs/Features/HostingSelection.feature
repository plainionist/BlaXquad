Feature: Hosting adapter selection
  squad-hq loads the window-hosting adapter used for a session from an explicit "--hosting <assemblyPath>;<typeName>"
  descriptor, exactly like it already loads its agent provider, and reports a clear, non-crashing diagnostic for
  every way selection can fail. squad-hq itself never references the stdio hosting plug-in and exposes no built-in
  "--ui" mode; the backend acceptance harness is the only caller that selects stdio, and it always does so through
  this explicit descriptor. Omitting "--hosting" resolves the packaged default Photino hosting plug-in through the
  very same runtime-loading mechanism, never a compile-time reference.

  Scenario: A duplicate --hosting option fails clearly
    When the operator launches Headquarters with the hosting option specified twice
    Then the launch fails with a hosting diagnostic containing "may only be specified once"

  Scenario: A missing --hosting value fails clearly
    When the operator launches Headquarters with the hosting option missing its value
    Then the launch fails with a hosting diagnostic containing "requires a value"

  Scenario: A malformed --hosting descriptor fails clearly
    When the operator launches Headquarters with a malformed hosting descriptor
    Then the launch fails with a hosting diagnostic containing "must be in the form"

  Scenario: A missing hosting assembly fails clearly
    When the operator launches Headquarters with a hosting assembly that does not exist
    Then the launch fails with a hosting diagnostic containing "not found"

  Scenario: An unloadable hosting assembly fails clearly
    When the operator launches Headquarters with a hosting assembly that exists but cannot be loaded
    Then the launch fails with a hosting diagnostic containing "could not be loaded"

  Scenario: An incompatible hosting type fails clearly
    When the operator launches Headquarters with an incompatible hosting type
    Then the launch fails with a hosting diagnostic containing "does not publicly implement"

  Scenario: A hosting type with no public parameterless constructor fails clearly
    When the operator launches Headquarters with a hosting type that has no public parameterless constructor
    Then the launch fails with a hosting diagnostic containing "has no public parameterless constructor"

  Scenario: A hosting constructor failure is surfaced clearly
    When the operator launches Headquarters with a hosting type whose constructor throws
    Then the launch fails with a hosting diagnostic containing "threw during construction"

  Scenario: An explicit valid hosting descriptor loads and completes the ready handshake
    When the operator launches Headquarters with an explicit valid stdio hosting descriptor
    Then Headquarters completes the ready handshake

  Scenario: A hosting plug-in deployed alongside duplicate contract assemblies still loads
    When the operator launches Headquarters with a hosting plug-in deployed alongside duplicate contract assemblies
    Then Headquarters completes the ready handshake

  Scenario: Omitting --hosting resolves the packaged default Photino factory
    When the operator launches Headquarters against an unconfigured project with hosting omitted
    Then the launch fails with a workspace diagnostic containing "Config not found" and no hosting diagnostic

  Scenario: An explicit Photino descriptor resolves the same packaged plug-in
    When the operator launches Headquarters against an unconfigured project with an explicit Photino hosting descriptor
    Then the launch fails with a workspace diagnostic containing "Config not found" and no hosting diagnostic
