# Architecture and Design Decisions

## Components
1. **URL API** — create a short code, redirect, and return analytics.
2. **UrlStore** — prototype in-memory repository. Concurrent dictionaries protect collection operations; click counts use atomic increments.
3. **WorkflowOrchestrator** — owns workflow state, dependency definitions, execution order, approval checkpoint and audit history.
4. **Agent adapters** — requirement, codebase, design, planning, coding, test, security and release roles. Current adapters are deterministic; replaceable interfaces are a recommended next step.
5. **Human approval endpoint** — pauses before release readiness is marked complete.

## Workflow graph
`normalize → (impact || design) → plan → implement → (test || security) → approval → release`

The two parenthesized groups can execute in parallel. `plan` waits for both impact and design; `approval` waits for test and security. A task runs only if all dependencies succeeded.

## State and governance
- Per-task states: Pending, Running, Succeeded, Failed, Blocked, AwaitingApproval, Skipped.
- Workflow states include Running, AwaitingApproval, SafeStopped and Completed.
- Ambiguity is recorded as assumptions and prompts a human decision.
- Release execution is deliberately not implemented. The system produces a release-readiness result only.
- Audit events record stage transitions and approval outcomes.

## Important prototype limitation
The code illustrates stateful orchestration and gates, but it is not a full production-grade agent runtime. The current deterministic task adapter does not call an LLM, the in-memory state is not durable, and the retry/rollback path is only a basic placeholder. In an interview, state these limitations openly and describe how you would implement durable state, idempotent workers, retry budgets, compensation actions and distributed tracing.

## Production evolution
- Persist workflow/task state and audit records in PostgreSQL or SQL Server.
- Use a durable orchestrator (e.g. Temporal, Durable Functions, or a carefully designed queue/worker model).
- Define typed agent contracts and structured outputs; validate schemas before accepting outputs.
- Use bounded retries with exponential backoff and jitter for transient failures only.
- Make task handlers idempotent; store attempt IDs and output hashes.
- Use policy checks before tools, file writes, package installation, network calls or deployment.
- Require authenticated RBAC approval for high-impact changes.
- Store secrets in a secret manager; never pass secrets into prompts or logs.
- Use OpenTelemetry trace IDs, structured logs, metrics and dashboards.
- Use transactional outbox / durable queue patterns for cross-service work.
- Roll back deployments via versioned artifacts and deployment strategy; do not assume database changes are always reversible.
