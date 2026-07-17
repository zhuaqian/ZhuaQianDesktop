# MCP Compatibility Research Spike (Epic E3)

Updated: 2026-07-17

This is a **research spike only**. Per the execution backlog, the project must
**not claim MCP support until a real MCP client exists**. This document records
what MCP is, why the current hook framework is the right seam, and what remains
before any claim could be made.

## What MCP is

Model Context Protocol (MCP) is an open protocol that lets a model/agent connect
to external "servers" that expose:

- **Tools** — functions the model can call.
- **Resources** — readable data sources.
- **Prompts** — reusable prompt templates.

Communication is typically JSON-RPC 2.0 over stdio or HTTP/SSE, with a capability
handshake (`initialize`), structured tool schemas, and content blocks.

## Why the hook framework is the seam

The Epic E hook framework (`src/Agent/Hooks/`) defines:

- `BeforeCommand` / `AfterCommand` around `AgentPipeline` execution.
- `BeforeFileWrite` before local file writes.
- `BeforeModelCall` / `AfterModelCall` around provider calls.

A future MCP client would map MCP tool calls onto `IAgentCommand` executors and
observe them through these hooks, rather than inventing a parallel execution path.
This keeps MCP behind the same `PermissionGate -> Executor -> Audit/Output`
pipeline as everything else.

## What is NOT done (must not be claimed)

- No MCP client implementation exists in this repository.
- No JSON-RPC transport, capability handshake, or tool-schema negotiation exists.
- No MCP server discovery or launch logic exists.
- The hook framework is a *prerequisite seam*, not MCP support.

## Open questions before a real spike

1. Transport: stdio child process vs HTTP/SSE vs WebSocket — which fits the
   WinForms single-process model best?
2. Schema mapping: how MCP tool input schemas map to `IAgentCommand.Parameters`.
3. Permission mapping: how an MCP tool's declared risks map to `PermissionGate`
   levels and the approval surface.
4. Trust: how an MCP server is vetted before it can register tools.

## Conclusion

Defer any MCP code until a concrete client is required. Today the correct, honest
statement is: "ZhuaQian Desktop has a plugin manifest contract and a hook framework
that MCP could plug into; MCP support is not implemented."
