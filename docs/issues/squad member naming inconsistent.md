---
title: squad member naming inconsistent
priority: 1
---

in the backend we now clearly differentiate between role and member.
conceptually there is still a one to one relationship but at least
in naming we make clear that in future it must not necessarily be the same

in the frontend we still use "rolepanel" and also in the css classes we use role.

even in backend we still find places not properly named

we should also cleanup/update glossary

a "role" is a stereotype. we have "role prompt" files defining characteristics of a role

but when we "instantiate" a squad member (with a role prompt), once it is "alive" it is a squad member.

with that definition there is no "role" in the UI, there is no "role state" and there
is no role transcript - its all about "squad members"
