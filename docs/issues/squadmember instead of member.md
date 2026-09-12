---
title: squadmember instead of member
priority: 1
---

classes which are about squad members and only start with "Member" like 
most classes in "src\squad.Application\Members" should actually start with "SquadMember"
for consistent naming

this additional folder "Members" is not needed - move all to root of this assembly
adjust namespaces accordingly

## Implementation plan

This is an internal terminology and source-organization refactor. Preserve runtime behavior, public APIs, CLI and UI
protocol field names, serialization, and the established lowercase prose term "member". Do not add compatibility
aliases for the renamed internal types.

### Slice 1: Qualify squad-member type names [done]

Make every `squad.Application` type whose name starts with `Member` and whose responsibility belongs to one configured
squad member use the unambiguous `SquadMember` prefix:

- rename `MemberAggregate`, `MemberEventProjector`, `MemberMessage`, `MemberProcessor`, and `MemberSnapshot`, together
  with their source files, to `SquadMemberAggregate`, `SquadMemberEventProjector`, `SquadMemberMessage`,
  `SquadMemberProcessor`, and `SquadMemberSnapshot`;
- rename the transcript types and their source files from `MemberTranscriptArchive` and `MemberTranscriptState` to
  `SquadMemberTranscriptArchive` and `SquadMemberTranscriptState`;
- rename `TranscriptStore`'s nested `MemberPublicationIdentity` type to `SquadMemberPublicationIdentity`;
- update all constructor calls, generic arguments, signatures, XML documentation references, and the architecture
  manual's `MemberEventProjector` reference to the new names.

Keep the existing namespaces and directory layout in this slice so the commit changes terminology only. Do not rename
message subclasses, properties, local variables, protocol DTOs, or persisted fields merely because they contain the
word "member".

Acceptance criteria:

- no type declaration beginning with `Member` remains in `squad.Application`;
- every renamed top-level type has a matching `SquadMember*.cs` source filename, and no stale references to the old
  type names remain;
- the solution builds and the existing backend specification suite passes without behavior or protocol changes.

**Status: complete (43ae9eb51f).** Internal squad-member types now use the `SquadMember` prefix, with matching
source filenames. Namespaces, directory layout, message subclasses, properties, protocol DTOs, and persisted fields
are unchanged.

### Slice 2: Flatten the application member implementation

Move every source file from `src/squad.Application/Members` into the `squad.Application` project root, including
`AbortLease`, `OperationLease`, and `PromptLease`. Change those files from the `squad.Application.Members` namespace
to `squad.Application`, remove obsolete namespace imports, and update any resulting references. Leave the
`Transcripts` component in its existing namespace and directory.

Acceptance criteria:

- `src/squad.Application/Members` no longer exists and the repository contains no
  `squad.Application.Members` namespace or import;
- all moved types retain their current visibility and behavior, with no forwarding namespace or duplicate types;
- the solution builds and the existing backend specification suite passes without behavior or protocol changes.
