---
title: json for handoffs
priority: 50
---

simplify parser for handoff files by switching to some standard format and parser like json or yaml

also consider simpler tooling for agents
when handing off commits it is typically "latest from own branch"
so we could have "squad handoff --latest-work" which detects branch and commit automatically
and agent only needs to provide minimal information
because i frequently see agents calling "--help" to understand how to write handoff
(we should probably also improve the constitution here)
