
## v2 

- https://x.com/dexhorthy/status/2087569590268391897
- modularization, component boundaries, deep modules
- build a knowledge base
  - architecture

## v3

- intelligent filtering of tool and read file output
  - when reading files i only want to see the line count
  - when running tools i want the full command line but we need to trim the output, 
    what about first 5 lines and last 5 lines?

- effort recording
  - sqlite db
  - tool args
  - must work from all sessions 
  - no hook deployed into repo

- sub-agents
  - i want to see their output but we need to make clear that it is sub-agent output in the transcript
  - some vertical bar like "quote"?

## CLI feature gap

- let copilot analyze CLI vs BlaXquad
  - what is missing

what is this "Todo added 15 items" - internal DB?


## other

- check "AI_info" tooling how it indexes the md files wrt front-matter and section headlines

- seems to be slower?
- visualize sub agents?

- when sub agent is started i want to see which model is used
- 
- we should not use "locks"
  - go lock free with async/await or queues - actor model?

- separate executable "host" and "cli" used by squad

- Roles and skills out of repository 
- Engineering rules (copilot instructions) out of repository 
- Repository specific customization 
- More focused roles: UT vs Gherkin, specifier vs architect, refactoring, frontend vs backend…
- No fixed workflow, decide agents based on task, start when needed

- Project manager is the orchestrator, always alive 

- support "analyzer" agent which can reverse-engineer code bases

- any event/output missing which copilot CLI shows?
  - anything missing SDK would provide?
- Tools for managing KB?
- KB should contain ADRs
- How to abstract KB and issue tracker?
- Store full telemetry, every tool call, every model call, every hand off in sqlite 
- Review agent templates from swarm-forge/squad 
- Measure everything related to productivity 
- Histogram of all files read
- Chose models dynamically based on complexity of task
- Role instructions are not compacted 
- Make role specific skills only available to those roles
- Auto-retro: apply learning to KB automatically, suggest tool improvement issues 
- Dedicated rules for improving the agent and engineering rules. Focus on clarity of intent, consistency, avoid redundancy. Check spec-driven development book. Make it a skill? Or even dedicated agent role?
- Issue template and checker - rule: clarifications go back to issue
- Integrate tools for visualization of design and architecture. How to support multiple languages? Abstraction: graphs! Dependencies, nesting/part of. Extend https://github.com/plainionist/arch-review. Only for human feedback - governance in fitness functions
- Arch visualization: https://github.com/almendili/skills/   https://codecharta.com/?utm_source=chatgpt.com 
- “Do not guess” rule - look up KB, glossary specs, if still not clear ask user
- Maybe we should have hardening agent trying to break the implemented issue. Engineering means figuring out what could possibly go wrong 
- Dry run mode: pretend build feature, max even change files but throw those away and just list gaps in spec - agent records decisions it has to make. Then improve spec or KB and the start.  Architect only? Implementer only?
- Add an “veteran” agent: doest change any file but answers any question to the project. Analyze specs, KB, reverse engineers decisions . Used by humans and agents 
- Consider place for ADRs in KB
- Learn from those skills to improve my agents
  - https://github.com/addyosmani/agent-skills
  - https://github.com/mattpocock/skills/tree/main


## Other

- Metadata-first knowledge retrieval
  
  - Search KB metadata before loading full documents.
  - Return only the most relevant document candidates.
  - Load full KB articles only when selected by an agent.

- Progressive skill loading
  
  - Expose only skill name, description, and usage hints initially.
  - Load full skill instructions only when the skill is selected.
  - Load skill references/resources only when needed.

- Lazy tool discovery
  
  - Do not expose every tool/MCP schema to every agent.
  - Make only role- and task-relevant tools available.

- Minimal tool output
  
  - Never inject large raw tool output into model context.
  - Store large results in files.
  - Filter/process results locally and return only relevant information.

- Large-result offloading
  
  - Redirect build logs, test output, search results, diffs, etc. to files.
  - Give agents a path plus a concise summary instead of the full output.

- Purpose-built discovery tools
  
  - Prefer tools that answer one engineering question well.
  - Reduce repeated grep/search/tool calls.
  - Examples: definitions, callers, relevant specs, architecture area, KB search.

- Fresh context at role handoffs
  
  - Architect → Implementer → Verifier should use separate contexts.
  - Transfer a structured handoff instead of the complete conversation history.
  - Include only goal, decisions, constraints, relevant knowledge, changed files, and open questions.

- Isolated agent/subagent contexts
  
  - Give each role, task, or parallel slice only the context it requires.
  - Avoid automatically inheriting the parent agent's full history.

- Context compaction
  
  - Replace long conversation history with structured current state when necessary.
  - Preserve goals, decisions, constraints, progress, evidence, and next actions.

- Context receipts
  
  - Record what context was supplied to every agent run.
  - Track KB articles, skills, tools, rules, handoff data, and selection reasons.
  - Make agent behavior reproducible and debuggable.

- Structured handoffs
  
  - Handoffs are explicit state transitions, not prose conventions.
  - Define required handoff data per role.
  - Detect agents stopping without a valid handoff.

- Needs Human / Needs You state
  
  - Represent unresolved questions, conflicts, or blocked decisions explicitly.
  - Agents must escalate rather than silently stop or guess.

- Deterministic lifecycle states
  
  - Distinguish implementation completion from verification and delivery.
  - Example: "In Progress → Completed → Verified → Accepted → Integrated → Delivered".

- Independent verification
  
  - Implementers cannot declare their own work verified.
  - Verification runs independently with separate context and responsibilities.
  - Quality gates determine whether work can progress.

- Quality gates
  
  - Tests.
  - Architecture fitness functions.
  - Static checks.
  - Required review/evidence.
  - Gates are deterministic wherever possible.

- Persistent recovery loops
  
  - Verification/review feedback returns to the responsible agent/task.
  - Preserve task state, branch/worktree, evidence, and relevant context across retries.

- Dependency-aware task execution
  
  - Architecture/specification can produce slices with explicit dependencies.
  - Execute ready slices in parallel where safe.
  - Use a DAG for complex missions.

- Isolated workspaces
  
  - Parallel implementation work should use isolated branches/worktrees/sandboxes.
  - Integration happens only after required gates pass.

- Model routing by task
  
  - Use expensive reasoning models for specification and architecture.
  - Use cheaper models for classification, cleanup, summarization, simple verification, and similar low-risk tasks.

- Cost and efficiency observability
  
  - Track tokens per mission.
  - Track tokens per role.
  - Track tokens per tool call.
  - Track tool calls per mission.
  - Track context size and compaction.
  - Track retries and failed agent loops.

- knowledge-loading principle
  
  - Search cheap metadata first. Load expensive content only after selection.

- context principle
  
  - Give every agent the minimum context required to perform its current responsibility.

## refactoring squad

assume we have refactoring task
use planner once to create dedicated slices - separate issue each
then we can use dedicated squad just doing those prompts in a loop


> process first issue (by file name numbering) from docs/issues/
> make sure all tests are still green after your changes
> when done say so on the console but don't delete the issue yet

> review the pending changes. get intent from the first issue (by file name numbering).
> you only review code - you dont run tests!
> goal achieved? pure refactoring? no behavioral changes?
> if yes: delete the issue and commit all changes
> if not: add your review findings to the issue 

> review findings added to the current issue!
> analyze and fix!

> review findings fixed - re-review

two fixed coder and reviewer sessions
only close until full focused story is done not after every slice
