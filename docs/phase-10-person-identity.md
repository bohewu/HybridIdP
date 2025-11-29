# Phase 10: Person - Multi-Account Identity & Profile

Status: In Progress (Phase 10.1 Complete ✅)

## Goal
Introduce a `Person` layer to represent the real-life identity (profile, employment history, employeeID, etc.) separate from `ApplicationUser` which represents an authentication account (username, external login, credentials, roles).

## Why
- Support a single person owning multiple accounts (e.g., employee has a contract account + permanent account).
- Allow username change / alias behavior while retaining a single person profile and history.
- Centralize profile and audit events at the Person level while keeping the existing account & role model.
- Make it easier to manage linked external logins (Google/Facebook) per account and allow role switching where needed.

## Design Summary
- New entity `Person` with fields: Id, FirstName, MiddleName, LastName, Nickname, EmployeeId, Department, JobTitle, ProfileUrl, PictureUrl, Website, Address, Birthdate, Gender, TimeZone, Locale, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy.
- Add `PersonId` GUID FK to `ApplicationUser` (nullable) so one Person can have multiple `ApplicationUser` accounts.
- Keep role assignments on `ApplicationUser` to preserve per-account permissioning.
- Optionally add indexes (e.g., `EmployeeId` unique) and `AuditEvents` for person-level updates.

## Incremental Implementation Plan (Small steps)
Phase 9 is intentionally designed as incremental tasks with tests at each step.

### Phase 10.1 - Schema & Backfill ✅ (Completed: 2025-11-29)
- ✅ Create `Person` entity and a new DB migration to add `Person` table.
- ✅ Add `PersonId` column on `ApplicationUser` (nullable) with FK.
- ✅ Implement a one-off migration script / data migration that creates a `Person` row for each existing `ApplicationUser` and sets `PersonId`.
- ✅ Add unit test for model and migration logic (create person from single user).

**Implementation Details:**
- Created `Core.Domain.Entities.Person` entity with full profile fields
- Generated migrations for both SQL Server and PostgreSQL
- Created backfill scripts: `scripts/phase10-1-backfill-persons-sqlserver.sql` and `scripts/phase10-1-backfill-persons-postgres.sql`
- Added 9 unit tests in `Tests.Infrastructure.UnitTests.PersonEntityTests` (all passing)
- Created automation script: `scripts/run-phase10-1-migration.ps1`
- Configured unique index on `EmployeeId` with nullable filter
- Set up `OnDelete: SetNull` relationship to preserve user accounts when person is deleted

### Phase 10.2 - Services & API (Backwards compatible)
- Add `IPersonService` interface and `PersonService` implementation.
- Add controller endpoints:
  - GET /api/admin/persons/{id}
  - POST /api/admin/persons
  - PUT /api/admin/persons/{id}
  - GET /api/admin/persons/{id}/accounts
  - POST /api/admin/persons/{id}/accounts (link existing account to person) - optional
- Add `UserManagementService.AttachPersonToUser` and `Detach`.
- Add unit tests for the new service and controller methods.

### Phase 10.3 - UI and E2E (Optional but recommended)
- Add a Person profile page in the Admin UI that lists linked accounts for that person, and a way to link/unlink accounts.
- Tests: E2E to create person, link account, unlink account, and ensure login flows still map to the correct user and person profile.

### Phase 10.4 - Optional Full Migration (Clean-up)
- Refactor code to prefer `Person` for profile reads/writes (move profile fields off `ApplicationUser`), then remove duplicated profile fields from `ApplicationUser`.
- Ensure all APIs reading user profile are updated to use `Person` where appropriate.
- Add integration tests for Person-centric queries & permissions.

## Possible further features (Phase 10.5+)
- Role switch / AssumeRole: Allow account owners to select which role to act as during a session (use claims or impersonation tokens, store active role in `UserSession`).
- External login binding management UI & API (link/unlink Google/Facebook to specific `ApplicationUser` accounts).
- Person-level audit and history UI.

## Security & Audit
- Moving to person profile requires careful authorization rules: only admin or profile owner (or those granted right) may update person profile.
- All aliasing, linking, and role switching must be audited (who performed the change, timestamp, reason).
- Ensure deletion/unlinking of accounts is safe and doesn't leave persons without a usable login (require at least one active credential per person/account as needed).

## Testing and Validation
- Unit tests: `PersonService` CRUD and attach/detach account use cases.
- Integration tests: Migration script and DB constraints (PersonId FK, unique constraints if used).
- E2E tests: Create person and link multiple user accounts; login via different accounts and verify person profile is consistent; link/unlink external provider; role switch tests.
- Performance tests: Keep read-path fast and ensure index on `PersonId` if queries frequent.

## Acceptance Criteria
1. Each `ApplicationUser` has `PersonId` assigned after migration; no user data is lost.
2. Create / Update Person APIs work and writes reflect across linked accounts.
3. Linking / unlinking accounts to/from person is audited and requires appropriate permissions.
4. External login linking still works per account; login maps to application user and its person.
5. Unit, integration, and E2E tests are added and passing.

## Rollout & Migration Strategy
- Soft deploys: start with schema + data fill script (Phase 9.1). Keep old profile fields until UI & code migrated.
- Once all reads/writes use `Person`, remove redundant columns from `ApplicationUser`.
- Coordinate with operations to plan DB migration downtime or transactional migration strategy for production.

## Delivery Plan
I will implement Phase 10 in small steps as described, starting with Phase 10.1 (migration + basic service + tests). Each step will include unit and E2E tests.

See `docs/PROJECT_PROGRESS.md` for status and checklists.

---

## Phase 10.1 Completion Summary ✅

**Date:** 2025-11-29

**Files Created/Modified:**
1. `Core.Domain/Entities/Person.cs` - New entity
2. `Core.Domain/ApplicationUser.cs` - Added PersonId FK
3. `Core.Application/IApplicationDbContext.cs` - Added Persons DbSet
4. `Infrastructure/ApplicationDbContext.cs` - Person entity configuration
5. `Infrastructure.Migrations.SqlServer/20251129020038_Phase10_1_AddPersonEntity.cs` - SQL Server migration
6. `Infrastructure.Migrations.Postgres/Migrations/20251129020038_Phase10_1_AddPersonEntity.cs` - PostgreSQL migration
7. `scripts/phase10-1-backfill-persons-sqlserver.sql` - SQL Server backfill script
8. `scripts/phase10-1-backfill-persons-postgres.sql` - PostgreSQL backfill script
9. `scripts/run-phase10-1-migration.ps1` - Automation script
10. `Tests.Infrastructure.UnitTests/PersonEntityTests.cs` - Unit tests (9 tests, all passing)

**Database Changes:**
- New table: `Persons` with 20 columns
- New column: `AspNetUsers.PersonId` (nullable, FK to Persons)
- Index: `IX_Persons_EmployeeId` (unique, filtered for non-null values)
- FK constraint: `FK_AspNetUsers_Persons_PersonId` with `OnDelete: SetNull`

**Test Results:**
- ✅ 9/9 unit tests passing
- ✅ All existing tests still passing
- ✅ Build successful

**Migration Scripts:**
- SQL Server: Transaction-based with rollback on error
- PostgreSQL: DO block with exception handling
- Both include verification and summary output

**Next Steps:**
- ✅ Phase 10.2: Implement IPersonService and API endpoints (COMPLETE)

---

## Phase 10.2: Services & API

### Status: ✅ COMPLETE

**Implementation Date:** 2025-11-29

**What was built:**

1. **Service Layer (`IPersonService`, `PersonService`)**
   - 11 methods: CRUD operations + account management
   - Search functionality (by name, employeeId, nickname)
   - Pagination support (skip/take)
   - EmployeeId uniqueness validation
   - Comprehensive logging via `ILogger`

2. **API Layer (`PersonsController`)**
   - 9 RESTful endpoints under `/api/admin/persons`
   - Admin authorization via `[Authorize]` attribute
   - DTOs: `PersonDto`, `PersonResponseDto`, `LinkedAccountDto`, `PersonListResponseDto`
   - Full CRUD + Account Linking operations

3. **Testing**
   - 17 unit tests in `PersonServiceTests` (all passing)
   - Tests cover: CRUD, linking, unlinking, search, pagination, validation
   - Uses EF Core InMemory database for isolation

**API Endpoints:**

```http
GET    /api/admin/persons              # List persons (paginated)
GET    /api/admin/persons/search       # Search by term
GET    /api/admin/persons/{id}         # Get specific person
POST   /api/admin/persons              # Create person
PUT    /api/admin/persons/{id}         # Update person
DELETE /api/admin/persons/{id}         # Delete person
GET    /api/admin/persons/{id}/accounts    # Get linked accounts
POST   /api/admin/persons/{id}/accounts    # Link account
DELETE /api/admin/persons/accounts/{userId} # Unlink account
```

**Files Created:**
- `Core.Application/IPersonService.cs` - Service interface
- `Infrastructure/Services/PersonService.cs` - Implementation (230+ lines)
- `Core.Application/DTOs/PersonDto.cs` - 4 DTOs for API
- `Web.IdP/Controllers/Admin/PersonsController.cs` - Admin API (340+ lines)
- `Tests.Infrastructure.UnitTests/PersonServiceTests.cs` - 17 tests

**Files Modified:**
- `Web.IdP/Program.cs` - Registered `IPersonService → PersonService` in DI
- `Tests.Infrastructure.UnitTests.csproj` - Added `Microsoft.EntityFrameworkCore.InMemory`

**Key Design Decisions:**

1. **Service Layer Validation**: EmployeeId uniqueness enforced at service level
2. **Account Linking**: Multi-account support with validation (prevent duplicate links)
3. **Search Flexibility**: Search across firstName, lastName, nickname, employeeId
4. **Pagination**: Efficient for large datasets (skip/take pattern)
5. **Audit Trail**: All operations logged for compliance

**How to Test (Manual):**

```powershell
# 1. Start IdP
dotnet run --project Web.IdP --launch-profile https

# 2. Get admin token (authenticate via browser or API)

# 3. Create person
$token = "YOUR_ADMIN_TOKEN"
Invoke-RestMethod -Uri "https://localhost:7035/api/admin/persons" `
  -Method POST `
  -Headers @{"Authorization"="Bearer $token"} `
  -ContentType "application/json" `
  -Body '{"firstName":"John","lastName":"Doe","employeeId":"E12345"}'

# 4. List persons
Invoke-RestMethod -Uri "https://localhost:7035/api/admin/persons" `
  -Headers @{"Authorization"="Bearer $token"}

# 5. Search persons
Invoke-RestMethod -Uri "https://localhost:7035/api/admin/persons/search?term=john" `
  -Headers @{"Authorization"="Bearer $token"}
```

**Next Steps:**
- Proceed to Phase 10.3: Implement Admin UI with Vue.js
- Create Person management pages
- Add E2E tests for Person workflows

---

## Phase 10.3: UI and E2E

### Status: 📋 PLANNED

**Goal:** Build Admin UI for Person management and add E2E tests

**Tasks:**

1. **Vue.js Components (Web.IdP/ClientApp)**
   - `PersonList.vue` - List persons with search/filter
   - `PersonDetail.vue` - View/edit person profile
   - `PersonCreate.vue` - Create new person
   - `LinkedAccounts.vue` - Manage account linking

2. **Router Configuration**
   - Add routes: `/admin/persons`, `/admin/persons/:id`, `/admin/persons/new`
   - Add navigation menu items

3. **API Integration**
   - Create `personApi.ts` service wrapper
   - Implement all CRUD operations
   - Handle errors and loading states

4. **E2E Tests (e2e/tests/admin/)**
   - `person-crud.spec.ts` - Test person CRUD operations
   - `account-linking.spec.ts` - Test account linking/unlinking
   - `person-search.spec.ts` - Test search functionality

**Success Criteria:**
- Admin can create/edit/delete persons via UI
- Admin can link/unlink accounts to persons
- Search functionality works correctly
- All E2E tests passing

---

## Phase 10.4: Optional Full Migration

### Status: 📋 PLANNED (OPTIONAL)

**Goal:** Move profile fields from `ApplicationUser` to `Person` as primary source

**Tasks:**

1. Update all APIs to read from `Person` instead of `ApplicationUser`
2. Migrate existing profile data from `ApplicationUser` to `Person`
3. Mark profile fields in `ApplicationUser` as deprecated
4. Eventually remove duplicated profile fields from `ApplicationUser`

**Considerations:**
- Breaking change for existing code
- Requires comprehensive testing
- May need feature flag for gradual rollout

---
End of Phase 10 design doc
