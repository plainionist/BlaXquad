Feature: State snapshot role order

  The dashboard must target the first configured role deterministically (for example, the issue explorer's Play
  action). "state.snapshot.roles" must therefore report roles in the exact order configured in "blaxquad/squad.json",
  not an incidental dictionary-enumeration order.

  Scenario: state.snapshot roles are reported in configured order, not alphabetical or insertion order
    Given a backend scenario configured with roles "reviewer,coder,writer"
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes state.snapshot roles reported in order "reviewer,coder,writer"
