# Repository Guidelines

## Project Context

HybridAuth IdP is a .NET 10+ Identity Provider built with ASP.NET Core, OpenIddict, EF Core, Razor Pages, and Vue 3/Vite.

Read `README.md` first for the current project overview, then use the docs under `docs/` for deeper context. Do not start implementation or review without reading the task-relevant documents below.

- `docs/DEVELOPMENT_GUIDE.md` for local setup and development workflow.
- `docs/ARCHITECTURE.md` for system structure.
- `docs/TESTING.md` for test clients and verification flows.
- `docs/DEPLOYMENT_GUIDE.md` for deployment behavior.
- `docs/SECURITY.md` for security expectations.
- `docs/FEATURES.md` for current feature behavior and product surface.
- `docs/OAUTH_FLOWS.md` when changing authorization, token, device, client credentials, consent, scopes, or redirect behavior.
- `docs/PERMISSION_SYSTEM.md` when changing RBAC, permissions, policies, admin authorization, or app roles.
- `docs/AUTHENTICATION_INTEGRATION.md` when changing external login, JIT provisioning, account linking, or upstream identity integration.
- `docs/DATABASE_CONFIGURATION.md` when changing EF Core configuration, migrations, provider behavior, seed data, or local database setup.
- `docs/MAINTENANCE_GUIDE.md` when changing operational behavior, monitoring, logging, background jobs, or runbooks.

When a task touches a specific domain, also inspect nearby docs under `docs/design/`, `docs/design_specs/`, `docs/implementation_plans/`, `docs/security/`, and `docs/examples/` if they exist for that area.

## Working Rules

- Keep changes scoped to the requested behavior. Do not refactor unrelated Identity, OpenIddict, EF Core, or Vue code while fixing a narrow issue.
- Treat authentication, authorization, token issuance, consent, session, MFA, redirect URI, and client-secret code as security-sensitive.
- Do not commit secrets, certificates, local environment overrides, database dumps, generated screenshots, or local agent/tooling state.
- Preserve existing user changes in the worktree. Do not reset, checkout, or delete unrelated files unless explicitly requested.
- Prefer existing service patterns, DTOs, extension methods, and tests before adding new abstractions.
- Use ASCII for new or edited files unless the file already requires localized text.

## Naming Conventions

- C# namespaces should mirror project and folder structure. Public types, methods, properties, records, enums, and constants use `PascalCase`; local variables and parameters use `camelCase`; private fields use `_camelCase`.
- Interfaces use the `IName` pattern and live close to their application boundary, for example `Core.Application/IClientService.cs` or `Core.Application/Interfaces/IPasskeyService.cs`.
- Async C# methods returning `Task`/`Task<T>` use the `Async` suffix, except framework-required method names and event handlers.
- DTO and API contract types should use explicit suffixes such as `Dto`, `Request`, `Response`, `Result`, or `Options`. Do not reuse EF entities as API request/response contracts.
- Service abstractions and implementations should pair predictably: `IFooService` in application contracts and `FooService` in `Infrastructure/Services/` or `Web.IdP/Services/` depending on ownership.
- Controllers use resource-oriented plural names where the API surface is resource-based, for example `ClientsController`, `UsersController`, and `ScopesController`.
- Tests should use descriptive names that state behavior and condition, following nearby test style. Prefer `Method_ShouldExpectedBehavior_WhenCondition` when adding new backend tests unless the file already uses a different consistent pattern.
- Vue component files use `PascalCase.vue`; composables use `useThing.js`/`useThing.ts`; shared utilities use focused `camelCase` module names. Keep locale keys stable and update both `en-US` and `zh-TW` locale trees when adding user-visible UI text.

## Design Guardrails

- Avoid fat controllers, Razor PageModels, Vue components, and services. If a file starts coordinating unrelated workflows, split by responsibility before adding more branching.
- Controllers and PageModels should handle HTTP/Razor concerns only: binding, authorization attributes, model validation, response shaping, and delegation. Business rules belong in application/infrastructure services.
- Vue components should primarily render state and emit user intent. Move API calls to service modules, reusable stateful logic to composables, and repeated UI to smaller components.
- Keep components and services cohesive around one domain concept. Avoid generic catch-all modules such as `AdminService`, `CommonHelper`, or `Utils` when a domain-specific name is available.
- Do not pass secrets, tokens, raw client secrets, or sensitive claims into frontend state unless the flow explicitly requires it and `docs/SECURITY.md` supports it.
- Do not broaden permissions, redirect URI validation, CORS, token lifetimes, cookie settings, MFA bypasses, or client-secret handling without an explicit security rationale and targeted tests.
- Prefer typed options, DTOs, validators, and existing extension methods over ad hoc dictionaries, stringly typed settings, or inline parsing.
- Keep EF Core queries explicit and cancellation-aware for request paths. Avoid lazy loading assumptions, broad `Include` chains, and loading full tables for admin lists.
- Keep generated assets out of hand edits. Vue source lives in `Web.IdP/ClientApp/src/`; generated bundles under `Web.IdP/wwwroot/dist/` are build outputs.
- For UI work, preserve the existing Tailwind/Vue design language, loading patterns, modal patterns, permission guards, and i18n structure before introducing new UI primitives.

## Backend Guidance

- OpenIddict server configuration lives primarily in `Web.IdP/Extensions/ServiceCollectionExtensions.cs`.
- ASP.NET Core pipeline and endpoint mapping live in `Web.IdP/Extensions/WebApplicationExtensions.cs`.
- OIDC/OAuth controllers live under `Web.IdP/Controllers/Connect/`.
- Client and scope persistence is managed through OpenIddict managers and application services under `Infrastructure/Services/`.
- Seed data lives under `Infrastructure/Seeding/`; keep seeded clients aligned with documented test flows.
- When changing OpenIddict endpoint behavior or permissions, verify discovery metadata, client permissions, and response content types.
- OAuth/OIDC protocol errors from backchannel endpoints must remain machine-readable JSON unless the relevant specification requires otherwise.

## Frontend Guidance

- Vue source lives in `Web.IdP/ClientApp/src/`; generated assets under `Web.IdP/wwwroot/dist/` should not be edited by hand.
- Keep admin UI changes consistent with the existing Tailwind/Vue component patterns.
- Do not introduce new frontend frameworks or icon systems unless requested.

## Reviewer Checklist

- Confirm the required docs for the touched area were read and the implementation does not contradict them.
- Check security-sensitive flows for machine-readable OAuth errors, content types, redirect validation, scope/client permissions, token/session behavior, and secret handling.
- Check naming, folder placement, DTO boundaries, cancellation token propagation, and consistency with nearby service/component patterns.
- Flag fat components, fat controllers, large methods, mixed responsibilities, duplicated domain logic, and UI logic embedded in API modules.
- Verify user-visible text is localized, generated files were not hand-edited, and both backend and frontend tests match the risk of the change.

## Verification

Use the narrowest meaningful verification for the change:

- Backend build: `dotnet build HybridAuthIdP.sln`
- Backend tests: `dotnet test HybridAuthIdP.sln`
- Frontend install/build/test from `Web.IdP/ClientApp` when UI code changes.
- For OIDC/OAuth changes, explicitly verify discovery metadata and affected endpoints such as `/connect/authorize`, `/connect/token`, `/connect/par`, `/connect/userinfo`, and logout/device endpoints when relevant.

## Deployment Notes

- Deployment files live under `deployment/`.
- Reverse proxy/WAF behavior may differ from local Kestrel behavior; validate backchannel OAuth endpoints independently from browser-based authorize flows.
- When diagnosing production auth failures, capture status code, `Content-Type`, OAuth error fields, and relevant headers without exposing client secrets or tokens.
