# Digital Scrum Board — Backend API

A production-grade agile project management REST API built with ASP.NET Core 9 and Entity Framework Core on SQL Server. It implements the full Scrum lifecycle — from epic and work item creation through sprint planning, execution, and completion — with strict role-based access control enforced at every layer. The system features HttpOnly cookie authentication, comprehensive audit logging, real-time SignalR broadcasting, and a multi-stage email verification and password security pipeline.

---

## Table of Contents

- [System Purpose & Domain](#system-purpose--domain)
- [Architecture & Design](#architecture--design)
- [Security Design](#security-design)
- [Workflows & Business Logic](#workflows--business-logic)
- [API Design & Integrations](#api-design--integrations)
- [Database & Data Handling](#database--data-handling)
- [Key Highlights](#key-highlights)

---

## System Purpose & Domain

The Digital Scrum Board API serves as the authoritative backend for a team-oriented agile workspace. It manages the complete Scrum hierarchy (Epics → Stories → Tasks), enforces a four-column Kanban workflow (To-do → Ongoing → For Checking → Completed), and orchestrates sprint lifecycles from planning through completion. Beyond data persistence, it acts as a real-time event bus — pushing board changes, notifications, and audit events to all connected clients the moment they occur.

---

## Architecture & Design

The system follows a clean, layered architecture that separates concerns across four distinct tiers:

- **Controllers** handle HTTP routing, input validation, and HTTP response shaping. They extract identity claims and delegate all business logic downstream.
- **Services** own domain rules and orchestration — sprint transitions, permission evaluation, notification dispatch, and audit logging all live here.
- **Repositories** encapsulate all data access, isolating EF Core queries from business logic and enabling unit testing boundaries.
- **Data / Models** define the domain schema, EF configurations, query filters (e.g., global soft-delete filter on WorkItems), and hierarchy validation rules.

All service registrations use scoped lifetimes, and DI is constructor-injected throughout. The request pipeline layers rate limiting, cookie authentication, and a custom session-validation middleware before authorization runs — ensuring disabled accounts and role-changed sessions are rejected on every request, not just at login.

---

## Security Design

Security is treated as a first-class architectural concern rather than an afterthought.

**Authentication** uses HttpOnly cookies managed by ASP.NET Core's cookie middleware. The session token is never exposed to JavaScript, making it immune to XSS-based session theft. Cookies are issued as session-only (non-persistent) until email verification completes, at which point they are reissued as persistent — a deliberate design that prevents unverified accounts from persisting sessions across browser restarts.

**Authorization** is enforced at multiple granularities. Attribute-level role guards (`[Authorize(Roles = "Administrator")]`) protect admin-only controllers. Within action methods, fine-grained checks evaluate sprint ownership, work item assignment, and team membership before any mutation is permitted. For example, moving a board card requires the requester to be either an Administrator, Scrum Master, the Sprint Manager of that sprint, or the work item's assigned developer — all evaluated in `BoardService.MoveWorkItemAsync` before any database write occurs.

**Session invalidation** is enforced on every authenticated request through a custom middleware that fetches the user's current role and team from the database and compares them against the session claims. If they diverge — because an administrator changed the user's role or team — the session is immediately invalidated server-side (`SignOutAsync`) and the client receives a structured `SESSION_INVALIDATED` response with a reason code. A corresponding SignalR event (`UserSessionInvalidated`) is simultaneously pushed to all of the affected user's connected tabs.

**Account lockout** is implemented through audit log analysis rather than a mutable lockout field. The system counts consecutive failed login attempts by scanning the audit log in reverse, stopping at the first successful login or admin unlock event. After five failures, a stepped cooldown is applied; after eight, the account is locked for 24 hours. This approach makes the lockout history tamper-evident and naturally produces a full audit trail.

**Password security** uses PBKDF2-SHA256 with 150,000 iterations and a 128-bit random salt, formatted as a versioned hash string (`v1.<iterations>.<salt>.<key>`). A legacy SHA-256 path supports hash migration for existing accounts. All password resets require a 6-digit time-limited OTP delivered via email, and the OTP verification endpoint applies the same stepped rate limiting as the login flow.

**Audit logging** is transactional for write operations. In `WorkItemRepository.AddWithAuditAsync`, the work item insert and its audit record are committed in a single database transaction, guaranteeing that no work item is created without a corresponding audit entry. Sprint and board actions log structured details including old/new values, actor identity, and IP address.

**Input validation** is applied at the DTO layer via Data Annotations and at the service layer via business rule checks. Priority and status values are normalized to canonical forms before persistence and constrained by SQL CHECK constraints at the database level, providing defense in depth against invalid data.

---

## Workflows & Business Logic

**Sprint lifecycle** enforces strict state transitions: Planned → Active → Completed. Starting a sprint requires all assigned work items to have an assignee; the API returns a structured conflict response listing unassigned items if this precondition fails. Completing a sprint automatically returns all non-completed work items to the backlog with their status reset to "To-do," executed within a database transaction. A confirmation round-trip is required for both stop and complete actions when unfinished items exist.

**Work item hierarchy** is enforced at the application layer in `DigitalScrumBoardContext.EnforceWorkItemHierarchyRulesAsync`, which intercepts EF SaveChanges calls, resolves the type of each modified work item, and validates parent-child relationships against the `WorkItemTypeHierarchyRules` table. Epics cannot have parents; Tasks cannot have children; Story parents must be Epics. This logic runs inside EF's change tracking, ensuring no hierarchy violation can slip through regardless of which code path triggers the save.

**Kanban board ordering** uses an integer `BoardOrder` field with optimistic concurrency via a SQL `rowversion` column. When a card is moved between columns, the source and destination columns are re-normalized in memory and the resulting `BoardOrder` updates are committed atomically. `DbUpdateConcurrencyException` is caught and surfaced as a user-facing conflict, prompting the client to refresh.

---

## API Design & Integrations

The API follows RESTful conventions with resource-oriented URLs (`/api/workitems/{id}/comments/{commentId}`), appropriate HTTP verbs, and structured error responses with machine-readable `code` fields alongside human-readable `message` fields. PATCH endpoints use partial update DTOs with explicit `ClearAssignee` flags to distinguish "not provided" from "intentionally null" — avoiding the ambiguity of nullable foreign keys in partial updates.

**Rate limiting** uses ASP.NET Core's built-in RateLimiter middleware with a fixed-window policy on login and OTP verification endpoints. The rejection handler writes a structured JSON body with a `retryAfterSeconds` field and sets the standard `Retry-After` response header.

**Email delivery** is abstracted behind `IEmailSender`, with `SmtpEmailSender` as the production implementation. Verification and password reset flows generate cryptographically random tokens (32-byte CSPRNG for verification links, 6-digit OTP for resets), store their SHA-256 hashes, and expire them after configurable durations.

**SignalR hubs** (`BoardHub`, `NotificationHub`) broadcast events to group-scoped clients. Sprint-specific updates go to `sprint-{id}` groups, user-specific notifications to `user-{id}` groups, and admin audit events to the `admins` group. Clients join and leave groups dynamically as they navigate the UI.

---

## Database & Data Handling

Entity Framework Core 9 with SQL Server is used throughout. The DbContext is configured with explicit Fluent API mappings, composite indexes for common query patterns (e.g., `(SprintID, Status)`, `(UserID, Timestamp)`), and global query filters for soft-deleted work items and comments. Migrations are tracked and versioned, with constraint additions handled as additive migrations to preserve rollback safety.

Performance-sensitive queries use `AsNoTracking()` for read paths. Tracked entities are fetched only when mutations are required. The sprint list endpoint paginates server-side and includes story/task counts via a single grouped aggregation query rather than N+1 fetches.

---

## Key Highlights

- Transactional audit logging ensures no data mutation occurs without a corresponding audit record
- Multi-layer permission system covering role, sprint ownership, team membership, and item assignment
- Session invalidation pushed in real time via SignalR when administrators change role or team assignments
- PBKDF2 password hashing with versioned hash format supporting future algorithm migration
- EF-level hierarchy rule enforcement as a cross-cutting interceptor on SaveChanges
- Optimistic concurrency on board card ordering using SQL `rowversion`
- Confirmation round-trips for destructive sprint operations with structured conflict responses
