---
title: show user responses in transcript
priority: 2
---

if agent asks user something a dialog is opened and the user can type or select a response.
that is all working  - just the response (whether selected or types) is missing in the transcript.
it is received by the agent - all working. i still would like to see it in the transcript printed 
including the green ">" as with regular user prompt

## Implementation plan

### Slice 1: Publish accepted input answers as user transcript entries

**Outcome:** When a user answers an agent input request by selecting a choice or submitting free-form text, the
owning role's transcript shows the exact answer as a normal user entry with the existing green `>` presentation.

1. Keep transcript authority in `squad.Application`. In the serialized input-completion path, append one sequenced
   transcript entry with source `user` and the submitted answer only after the pending request has been validated
   and `IAgentSession.RespondToInputAsync` has completed successfully. Publish that update before later queued agent
   output so the answer remains ordered ahead of the response it enables.
2. Reuse the existing role transcript mutation and notification path so the entry participates in live updates,
   synchronization, retention, paging, and reconnect recovery. Do not synthesize the answer in Vue or duplicate the
   rule in provider implementations.
3. Leave permission and elicitation responses unchanged. Wrong-role, duplicate, late, cancelled, or provider-failed
   input responses must not create a user entry; the transcript mutation belongs strictly to successful input
   completion.
4. Extend the interaction acceptance coverage through the real UI protocol for both supported answer paths:
   a fixed choice (`wasFreeform: false`) and a typed answer (`wasFreeform: true`). For each, prove that the agent
   receives the response and that the owning role receives exactly one transcript update with source `user` and the
   exact answer.
5. Add focused dashboard coverage proving that such a `user` transcript entry renders the answer with prefix `>`
   and the existing green user styling. No Vue production change is expected unless this observable contract is not
   already met.

**Acceptance criteria**

- Selecting an offered answer records that answer once in the requesting role's transcript after it is accepted.
- Submitting a free-form answer records the exact submitted text once in the requesting role's transcript after it
  is accepted.
- Both entries use transcript source `user`, appear before subsequent agent output, survive transcript
  synchronization, and render with the same green `>` prefix as a regular user prompt.
- Rejected or failed input-response commands do not add transcript entries and retain their existing protocol and
  pending-interaction behavior.
