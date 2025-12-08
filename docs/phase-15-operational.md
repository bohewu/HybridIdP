# Phase 15: Operational Excellence & Observability

## 1. Overview
Empower devops and administrators with real-time insight into system health and runtime-configurable logging.

## 2. Health Checks UI
**Goal**: Visual dashboard at `/health-ui` showing status of all critical dependencies.

### 2.1 Health Checks
- **Database**: Check SQL Server / PostgreSQL connectivity.
- **Redis**: Check cache connectivity.
- **OpenIddict**: Self-check (check if core services are resolvable).
- **Disk Storage**: Warning if free space < 100MB (optional).

### 2.2 UI Implementation
- **Library**: `AspNetCore.HealthChecks.UI`
- **Storage**: `InMemory` (no need to persist history across restarts for now).
- **Endpoint**: `/health-ui` (Protected by `RequireAdminRole`).
- **JSON Endpoint**: `/health` (Protected or Public safe-list).

## 3. Configurable Structured Logging
**Goal**: Switch to Serilog and allow Admins to change log levels **without restarting** the application.

### 3.1 Serilog Integration
- Replace default `ILogger` with Serilog.
- **Sinks**:
    - `Console`: For container logs (standard).
    - `Seq` or `OpenTelemetry`: For centralized logging (configurable via connection string).

### 3.2 Dynamic Configuration UI
- **Requirement**: "Configurable by UI".
- **Design**:
    - Create `LogSettings` table or JSON blob in `SystemSettings`.
    - Key columns: `Namespace` (e.g., "Microsoft", "HybridIdP"), `Level` (Debug, Info, Warning).
    - **Middleware / Service**: `DynamicLoggingLevelSwitch`.
        - A background service or `IOptionsMonitor` change listener that updates the `LoggingLevelSwitch` singleton when settings change.
- **Admin UI Page**: `System > Logging`
    - Table of current overrides.
    - "Add Override" modal (Select Namespace, Select Level).
    - "Apply Changes" button (Triggers reload).

## 4. Deliverables
- [ ] `/health-ui` dashboard.
- [ ] Serilog configured.
- [ ] Admin Page for Log Level Management.
- [ ] Backend logic to apply log levels dynamically.
