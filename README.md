# Agentic Software Engineering System — URL Shortener

A runnable .NET 8 prototype for the assessment. It demonstrates:
- URL-shortener APIs and click analytics
- A stateful workflow planner with a dependency graph
- Parallelizable planning tasks, validation gates, bounded retry, safe-stop and approval checkpoints
- Three scenarios: greenfield, brownfield and ambiguous requirements
- Audit events and basic workflow metrics

> This is an assessment prototype, not a production deployment. The default workflow uses deterministic agents so it can run without an LLM key. See `docs/ARCHITECTURE.md` and `docs/INTERVIEW_GUIDE.md`.

## Prerequisites
- .NET 8 SDK

## Run
```bash
cd src/AgenticUrlShortener
dotnet restore
dotnet run
```
Open the Swagger endpoint shown in the console (usually `/swagger`).

## Core API examples
Create a short URL:
```bash
curl -X POST http://localhost:5000/api/urls \
  -H "Content-Type: application/json" \
  -d '{"url":"https://example.com/products?id=42","customCode":null}'
```
Use the returned `code` at `/r/{code}` to redirect. Click events are counted in memory.

Run an orchestration scenario:
```bash
curl -X POST http://localhost:5000/api/workflows \
  -H "Content-Type: application/json" \
  -d '{"scenario":"greenfield","requirement":"Create a URL shortener with click analytics"}'
```
Supported scenarios: `greenfield`, `brownfield`, `ambiguous`.

Inspect a workflow:
```bash
curl http://localhost:5000/api/workflows/{workflowId}
```
Approve a workflow paused at an approval gate:
```bash
curl -X POST http://localhost:5000/api/workflows/{workflowId}/approve
```

## Design boundaries
- In-memory storage: data is lost on restart; use a persistent store for production.
- No real LLM is called by default. Agent contracts are intentionally replaceable.
- Approval is represented by an API endpoint; production should use authenticated RBAC and durable approvals.
- The prototype does not deploy code or execute arbitrary generated shell commands.
- For production, add rate limiting, abuse detection, safe browsing, expiration policy, key management, distributed analytics, structured telemetry and CI/CD integration.

## Build and verify
```bash
dotnet build AgenticUrlShortener.sln
dotnet run --project src/AgenticUrlShortener/AgenticUrlShortener.csproj
```
The package currently includes a manual verification checklist in `docs/TESTING.md`; a separate automated test project is a recommended next step. Do not claim automated tests passed unless you add/run them and capture the results.
