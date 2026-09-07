---
title: after cancel - user prompt missing
priority: 1
---

# Preserve a prompt submitted after cancellation

## Symptom

Sometimes, after cancelling a working agent and immediately submitting another prompt, the new prompt is not shown
in the transcript.

The draft is cleared as soon as Vue sends `prompt.send`, so the missing transcript row is also the only durable UI
evidence of what was submitted.

## Expected behavior

Once the application accepts a prompt after cancellation, the transcript contains that prompt exactly once. Events
from the cancelled turn remain excluded, and the replacement prompt is not itself cancelled by the preceding abort.

## Analysis

### The transcript does not own submitted prompts

`useDashboardSession.sendPrompt` sends `prompt.send` and immediately clears the Vue draft. The command is routed to
`SquadViewModel.SendAsync`, but that path only marks the role as working and invokes `IAgentSession.SendAsync`; it does
not add the submitted text to the authoritative transcript.

The user row is added later and indirectly:

1. `CopilotSdkRuntimeSession.SendAsync` submits the prompt to the Copilot SDK.
2. The SDK may publish a later `UserMessageEvent` echo.
3. `CopilotSdkClient` translates the echo to `AgentUserMessageEvent`.
4. The session event observer enqueues that event in `SquadViewModel`.
5. `AgentEventProjector` finally appends the `user` transcript entry.

The application therefore treats a provider echo as the source of truth for a command that the application itself
accepted. If the echo is omitted or rejected, there is no transcript entry to recover through synchronization.

### Cancellation creates an event-admission race

`RoleOperationCoordinator.TryBeginAbort` invalidates all ordinary events for the role and cancels its active local
operation. `SquadViewModel.ShouldIgnoreEvent` drops every ordinary event, including `AgentUserMessageEvent`, while that
role-wide invalidation flag is set. A later prompt waits for the in-flight abort and calls `ResumeEvents` before
invoking the provider.

That ordering is correct only after `AbortAsync` has established its barrier and only if the SDK echo occurs after
event admission is resumed. The real Photino boundary does not explicitly preserve that admission order: its
web-message callback starts `ReceiveMessageAsync` fire-and-forget, so adjacent `role.abort` and `prompt.send` handlers
can overlap. The provider's abort completion and event stream are asynchronous as well. Fire-and-forget dispatch is
evidence of a race surface, not proof that Photino reordered the two messages in the reported occurrence.

The likely failing interleaving is:

1. Vue sends `role.abort`, followed quickly by `prompt.send`.
2. The two native command tasks overlap and, because no command sequence is carried into role admission, the prompt
   can win admission before cancellation establishes the intended barrier.
3. Abort invalidates the role and cancels the operation that now owns the replacement prompt.
4. The SDK's `UserMessageEvent` for that prompt arrives during invalidation, or the aborted SDK operation never emits
   it.
5. No `AgentUserMessageEvent` is projected, while Vue has already discarded the draft.

This interleaving is consistent with the intermittent symptom, but must be confirmed with a boundary-level
reproduction. Regardless of which operation wins, visibility currently depends on scheduling among two UI command
tasks, the abort RPC, the send RPC, and the SDK event callback.

There is a related correctness gap in the opposite direction. Invalidation is role-wide rather than turn-scoped. As
soon as a new prompt calls `ResumeEvents`, a delayed event from the cancelled turn can be accepted as if it belonged
to the new turn. The current mechanism cannot distinguish a stale old-turn event from the new prompt's echo.

### Existing tests do not cover the production contract

The ViewModel feature covers that a prompt started during an in-flight abort waits before `SendAsync`, and separately
that events emitted while a role is cancelled are ignored. It does not combine those behaviors and assert the
replacement prompt's transcript entry.

More importantly, `RecordingAgentSession.SendAsync` records the call but does not emit `AgentUserMessageEvent`.
Consequently, the prompt/cancellation scenarios can pass without exercising the production mechanism that creates a
user transcript row. Photino protocol scenarios also await one command at a time and do not characterize adjacent
fire-and-forget messages.

The Vue transcript feed is not the first suspected owner: if no `TranscriptUpdate` is produced by the ViewModel,
snapshot recovery has no missing entry to restore.

## Required change

Make the accepted prompt authoritative before crossing the provider boundary:

1. After the preceding abort barrier has completed and the new per-role operation is admitted, append the submitted
   prompt to the C# transcript exactly once, immediately before starting the provider send.
2. Treat the SDK `UserMessageEvent` for a locally submitted prompt as an acknowledgement/echo and suppress or
   correlate it so it cannot create a duplicate row. Preserve genuinely external user events if the provider can
   produce them.
3. Give prompt/abort admission an explicit per-role order or generation. An abort may cancel only the operation that
   preceded it; a prompt ordered after that abort must wait for it and belong to the next generation.
4. Admit provider events against that operation generation rather than reopening a role-wide boolean gate. Delayed
   events from the cancelled generation must remain stale after the next prompt starts.

Do not serialize complete UI command tasks globally: a prompt operation can remain active for the whole agent turn,
and abort must still be able to interrupt it. Only command admission/order needs serialization; provider I/O and
different roles may continue concurrently.

Whether a provider send later fails should be represented explicitly, but it must not silently erase an already
accepted prompt. If command acceptance itself fails, the UI needs a correlated rejection before clearing the draft;
the current uncorrelated `protocol.error` is not sufficient to restore it reliably.

## Acceptance scenarios

- Hold an active turn, begin abort, submit a second prompt while abort is blocked, then complete abort. The provider
  receives the second prompt and its user transcript entry appears exactly once.
- Deliver the second prompt's provider echo while abort completion and event processing are deliberately interleaved.
  The accepted prompt remains visible exactly once.
- Deliver assistant, tool, idle, and user events from the cancelled generation after the second prompt starts. None
  mutate the replacement generation or its transcript.
- Send adjacent `role.abort` and `prompt.send` messages through the real Photino command boundary. Their admission
  order is deterministic without preventing abort from interrupting an active prompt.
- Make the replacement send fail before and after provider acceptance. The UI either retains the draft on rejection
  or shows the accepted prompt plus an explicit failure; it never loses the text silently.

Use the existing black-box Gherkin suite for the ViewModel and protocol ordering, plus a focused Playwright scenario
for draft clearing and visible transcript behavior. For diagnosis, temporarily trace SDK `user.message` and abort
events alongside the ViewModel admission decision. The existing `BLAXQUAD_SDK_EVENT_TRACE` cannot provide that
evidence because it currently records tool events only.

