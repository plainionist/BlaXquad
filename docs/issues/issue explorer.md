---
title: issue explorer
priority: 9999
---

introduce a toolbar above the agents.
similar like a menu bar it should show left "Issues"
when i click on it like a menu opens and i see all issues from docs/issues/
ordered by priority from frontmatter, i see title from frontmatter,
if no frontmatter is there order show filename

when i hover over it with mouse items get selected, when selected there is "fly out" showing frontmatter and first 5 lines of the issue

next to each entry there two icons 
- copy icon which copies the relative path to the workspace root of the issue to cliport
- a play icon which pushes the prompt "process this issue: '<relative path>'" to the first role configured.
  user still has to click enter - so this is frontend only

if there are no issues show a disabled entry "<no issues>"

