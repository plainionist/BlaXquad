---
title: handoff cleanup
priority: 1
---

handoff documents do not need versioning & LegacyHandoffQueueGuard is not needed
because on "launch" we simply clear all remaining handoffs

==> add this cleanup at startup
==> remove the now dead code mentioned above
