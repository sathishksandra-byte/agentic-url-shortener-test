# Three Required Scenarios

## 1. Greenfield
**Input:** “Create a URL shortener with click analytics.”

**Normalize:** HTTP/HTTPS URLs only; generated or optional custom code; redirect endpoint; total click count and bounded recent events.

**Task graph:** normalize → parallel impact analysis/design → plan → implement → parallel tests/security → human approval → release readiness.

**Validation:** invalid URL rejected; duplicate custom code rejected; unknown code returns 404; valid code redirects; click counter increments; security review flags open redirect abuse and rate-limiting needs.

**Decision:** keep implementation and release readiness separate. The prototype does not deploy itself.

## 2. Brownfield
**Input:** “Add custom aliases to an existing URL shortener without breaking old links.”

**Codebase reasoning:** inspect route, persistence model, validation, collision behavior, tests and existing redirect flow. In a real brownfield repository, the CodebaseAgent should index these files and identify impacted modules before proposing edits.

**Plan:** add optional alias field → validate allowed characters/length → enforce unique constraint → preserve generated codes → add regression tests → run compatibility checks → approval.

**Validation:** existing links continue redirecting; duplicate aliases return conflict; invalid aliases return 400; race conditions are covered by a unique database constraint.

**Governance:** generated code is a proposal until tests pass and a reviewer approves it.

## 3. Ambiguous requirement
**Input:** “Make links secure and keep analytics.”

**Ambiguities:** Does secure mean expiration, password protection, abuse scanning, allow-listed domains, or all of them? What analytics fields and retention are expected? Who can access analytics?

**Safe behavior:** record unknowns, propose options, identify security/privacy impact, and pause before implementation/release decisions that depend on the answers.

**Prototype assumptions:** analytics is total clicks plus a bounded recent sample; expiry policy remains unresolved. Human approval is required.

**Validation:** no unapproved expiry behavior is silently introduced; assumptions are visible in workflow output; the workflow cannot pass the approval gate without an explicit approval call.
