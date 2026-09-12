---
title: multiple dictionaries
priority: 10
---

identify types with multiple members of dictionaries with same key.
example: src\squad.AgentProvider.Fake\Control\ObservationJournal.cs
example: src\squad.Application\Members\MemberAggregate.cs

this is typically an indicator that we should rather have another type 
encapsulating the various aspects and then only have one member with collection
or dictionary of the new type

analyze the code base - identify such cases and suggest design improvements 
to streamline ownership
