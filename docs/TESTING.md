# Testing Approach and Manual Verification

## Commands
```bash
dotnet build
dotnet test
```

Do not claim tests passed unless you ran them in the target environment.

## URL API checklist
1. POST a valid HTTPS URL; expect 201 and a short code.
2. POST a malformed URL or `javascript:` URL; expect 400.
3. Reuse a custom code; expect 409.
4. Request an unknown code; expect 404.
5. Redirect a valid code; expect 302 and incremented analytics.
6. Call analytics; verify count and bounded recent click events.
7. Send concurrent requests against the same URL and confirm no lost increments.

## Orchestration checklist
1. Start a greenfield workflow; verify normalize, impact, design, plan, implementation, test and security stages.
2. Confirm impact and design can be independently eligible and the plan waits for both.
3. Confirm workflow pauses at `AwaitingApproval`.
4. Approve it and verify release-readiness completion.
5. Start ambiguous scenario; verify assumptions and approval question appear.
6. Start an unsupported scenario; verify safe default behavior is documented.
7. Inspect audit events for task start/success and approval records.

## Production tests to add
- Unit tests for URL validation, code generation, collision handling and analytics.
- Integration tests for HTTP status codes and redirect behavior.
- Property/fuzz tests for custom-code validation.
- Load tests for redirect latency and analytics write throughput.
- Fault-injection tests for worker timeout, retry exhaustion, stale workflow state and dependency failure.
- Security tests for SSRF/open redirect policy, abuse/rate limits, log injection, authorization and secrets handling.
- Durable-state recovery tests after process restart.
