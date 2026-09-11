Feature: State snapshot role order

  "state.snapshot.roles" must report roles in the exact order configured in "blaxquad/squad.json", not an incidental
  dictionary-enumeration order - independent of which role is the configured leader (see "Configured squad leader"
  for how the dashboard's Play action targets that leader by name rather than by position).

  Scenario: state.snapshot roles are reported in configured order, not alphabetical or insertion order
    Given a backend scenario configured with roles "reviewer,coder,writer"
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes state.snapshot roles reported in order "reviewer,coder,writer"
