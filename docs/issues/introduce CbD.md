---
title: introduce CbD
priority: 2
---


## 1

replace pre-cond check like
        ArgumentNullException.ThrowIfNull(members);
with usage of "Contract" class from squad.Domain

BUT as we use "nullable reference types" feature we do not need to check for null of reference types right?
in this case - simply remove this check

## 2

then check all public APIs - methods, constructors ...
which ADDITIONAL meaningful pre-cond we can add to find API misuse fast

## 3

then check every non-trivial class whether it would benefit from
introducing Contract.Invariant
again to find possible bugs fast

replace existing invariant checks which use if-then and throw standard exceptions e.g.
InvalidOperationException with Contract.Invariant

