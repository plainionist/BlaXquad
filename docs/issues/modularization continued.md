---
title: modularization continued
priority: 3
---

## 1

rename "squad.CopilotSdk" to "squad.AgentProvider.CopilotSdk"
adapt namespaces accordingly

## 2

src\squad.Specs\Support\Agents move to separate assembly called "squad.AgentProvider.Fake"
adapt namespaces accordingly
make internal what be could be made internal

## 3

update docs/manual
esp docs\manual\modules.md