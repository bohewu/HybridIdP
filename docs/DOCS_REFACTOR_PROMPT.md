# Documentation Refactoring Prompt

## 📋 Objective

Refactor the `docs/` directory to reduce file count, remove obsolete documentation, and keep only essential materials. Current state: **44 files** (692KB total). Target: **~15-20 essential files**.

## 🎯 Goals

1. **Reduce file count** - Consolidate related documents
2. **Keep only essentials** - Remove detailed implementation notes for completed phases (git history is sufficient)
3. **Preserve critical information** - Architecture, setup guides, and active TODOs must remain
4. **Improve discoverability** - Clear naming and organization

## 📁 Current File Analysis

**Critical Files (MUST KEEP):**
- `ARCHITECTURE.md` (21KB) - Core system architecture
- `DEVELOPMENT_GUIDE.md` (33KB) - Setup and development workflow
- `PROJECT_PROGRESS.md` (36KB) - Progress tracking and recent updates
- `FEATURES.md` (19KB) - Feature overview
- `DATABASE_CONFIGURATION.md` (24KB) - Database setup
- `SECURITY_HARDENING.md` (9KB) - Security implementation (Phase 11.6)
- `README.md` (13KB) - Entry point

**Important Reference (KEEP or CONSOLIDATE):**
- `AUTHENTICATION_INTEGRATION.md` (19KB) - Merge key points into ARCHITECTURE.md
- `PERSON_MULTI_ACCOUNT_ARCHITECTURE.md` (13KB) - Merge into ARCHITECTURE.md or FEATURES.md
- `SSO_ENTRY_PORTAL_ARCHITECTURE.md` (17KB) - **KEEP for Phase 12 planning**, extract TODOs to TODOS.md

**Phase Documents (ARCHIVE MOST):**
- Phase 1-9: Completed, can be archived (keep only brief summary in PROJECT_PROGRESS.md)
- Phase 10: Recently completed, keep `phase-10-person-identity.md` (31KB) as reference
- Phase 11: Keep `phase-11-account-role-management.md` (24KB) as reference
- Phase 12: Keep for future planning `phase-12-admin-api-hr-integration.md` (19KB)

**Detailed Implementation Prompts (DELETE or ARCHIVE):**
- `phase-11-implementation-prompt.md` (21KB) - Implementation done, no longer needed
- `phase-11-4-ui-implementation-prompt.md` (28KB) - Delete
- `phase-11-5-e2e-tests-prompt.md` (18KB) - Delete
- `phase-11-5-implementation-summary.md` (11KB) - Delete
- `phase-11-5-test-results.md` (7KB) - Delete
- `REFACTOR_JIT_PROVISIONING_PROMPT.md` (25KB) - Archive or delete
- `phase-10-1-quickstart.md` (7KB) - Consolidate into main phase-10 doc

**Test/Debug Documents (CONSOLIDATE or DELETE):**
- `API_RESOURCE_TEST_RESULTS.md` (10KB) - Delete (tests passing, no longer needed)
- `E2E_TEST_FAILURES.md` (8KB) - Delete (issues resolved)
- `E2E_TEST_CLIENT_CREDENTIALS.md` (2KB) - Merge into E2E_LOCAL_SETUP.md
- `NEXT_SESSION_PERSON_E2E_TESTS.md` (7KB) - Delete (Phase 10.5 completed)

**Legacy/Historical (MOVE TO ARCHIVE):**
- `PROJECT_STATUS.md` (92KB!) - Too large, extract key points to PROJECT_PROGRESS.md, archive rest
- `admin-ui-phase-3.md` (5KB) - Archive
- `phase-1-database-ef-core.md` (1KB) - Archive
- `phase-2-openiddict-oidc.md` (1KB) - Archive
- `CHANGELOG.md` (1KB) - Archive or delete (git log is sufficient)

**Specialized Topics (KEEP but CONSOLIDATE):**
- `MONITORING_BACKGROUND_SERVICE.md` (7KB) - Keep or merge into FEATURES.md
- `SCOPE_AUTHORIZATION.md` (13KB) - Keep or merge into FEATURES.md
- `phase-9-scope-authorization.md` (21KB) - Consolidate with SCOPE_AUTHORIZATION.md

**Planning Documents (KEEP):**
- `backlog-and-debt.md` (1.5KB) - **MUST KEEP** - Rename to `TODOS.md` and add Phase 12 items
- `notes-and-guidelines.md` (0.5KB) - Keep or merge into DEVELOPMENT_GUIDE.md
- `phase-12-admin-api-hr-integration.md` (19KB) - **Extract TODOs** to TODOS.md, then keep for reference

**Large/Obsolete:**
- `idp_req_details.md` (60KB!) - Archive (historical requirements, no longer relevant)

## ✅ Refactoring Plan

### Step 1: Create TODOS.md
```bash
# Rename and update backlog
mv docs/backlog-and-debt.md docs/TODOS.md
```

**Important:** Update TODOS.md to include:
1. Current items from backlog-and-debt.md
2. **Phase 12 planning items** (extract from `phase-12-admin-api-hr-integration.md` and `SSO_ENTRY_PORTAL_ARCHITECTURE.md`):
   - **Phase 12.1**: Admin API Endpoints (Person CRUD, User management, Role assignment)
   - **Phase 12.2**: OAuth 2.0 Client Credentials flow for machine-to-machine auth
   - **Phase 12.3**: Webhook support for real-time HR sync events
   - **Phase 12.4**: Bulk operations API (batch user provisioning, bulk role updates)
   - **Phase 12.5**: Audit logging for all admin API operations
   - **Phase 12.6**: API rate limiting and IP whitelisting
   - **Phase 12.7**: Reconciliation API for periodic full sync
   - **Phase 12.8**: External IdP integration (LDAP/AD federation)
   - **Phase 12.9**: SSO Entry Portal (統一應用程式入口)
     - Create standalone SSO Portal app (Next.js/React/Vue)
     - Register as OIDC client to IdP
     - Display app catalog with role-based filtering
     - Implement "Launch" buttons for seamless SSO
     - Application catalog management
3. Extract any outstanding TODOs from PROJECT_STATUS.md before archiving
4. Update last-updated date to 2025-12-03
5. Add section "## Completed Phases" with summary of Phase 1-11.6

### Step 2: Consolidate Architecture Documents
**Create: `ARCHITECTURE_CONSOLIDATED.md`** (merge and keep essentials from):
- ARCHITECTURE.md (base)
- AUTHENTICATION_INTEGRATION.md (merge auth flow section)
- PERSON_MULTI_ACCOUNT_ARCHITECTURE.md (merge person/account model)
- SSO_ENTRY_PORTAL_ARCHITECTURE.md (merge SSO flow)

**Result:** Single comprehensive architecture doc (~40-50KB)

### Step 3: Consolidate Feature Documents
**Create: `FEATURES_AND_CAPABILITIES.md`** (merge):
- FEATURES.md (base)
- MONITORING_BACKGROUND_SERVICE.md (add as subsection)
- SCOPE_AUTHORIZATION.md (add as subsection)
- phase-9-scope-authorization.md (extract key points)

**Result:** Single feature reference (~30-40KB)

### Step 4: Consolidate E2E Testing Docs
**Update: `E2E_LOCAL_SETUP.md`** (merge):
- E2E_TEST_CLIENT_CREDENTIALS.md (add credentials section)
- Keep setup instructions only

**Delete:**
- E2E_TEST_FAILURES.md (issues resolved)
- NEXT_SESSION_PERSON_E2E_TESTS.md (Phase 10.5 completed)
- API_RESOURCE_TEST_RESULTS.md (tests passing)

### Step 5: Consolidate Phase Documents
**Keep only these phase docs:**
- `phase-10-person-identity.md` (31KB) - Recent major feature
- `phase-11-account-role-management.md` (24KB) - Recent major feature
- `phase-12-admin-api-hr-integration.md` (19KB) - Future planning
- `SSO_ENTRY_PORTAL_ARCHITECTURE.md` (17KB) - Future planning (Phase 12.9)

**Archive to docs/archive/phases/:**
- phase-1 through phase-9 documents
- phase-10-1-quickstart.md
- phase-10-6-person-identity-verification.md
- phase-11-4-ui-implementation-prompt.md
- phase-11-5-*.md (all 3 files)
- phase-11-6-remove-role-switch-refactor-homepage.md (implementation done)
- admin-ui-phase-3.md

### Step 6: Clean Up Implementation Prompts
**Delete (implementation complete, git history sufficient):**
- REFACTOR_JIT_PROVISIONING_PROMPT.md
- phase-11-implementation-prompt.md
- All phase-11-5-* files

### Step 7: Handle Large/Obsolete Files
**Archive to docs/archive/historical/:**
- PROJECT_STATUS.md (92KB) - Extract critical TODOs to TODOS.md first
- idp_req_details.md (60KB) - Historical requirements
- CHANGELOG.md - Git log is sufficient

### Step 8: Update README.md
Update the main README.md to reference the new consolidated structure:
```markdown
## 📚 Documentation Structure

**Essential Reading:**
- [README.md](README.md) - Project overview and quick start
-- [ARCHITECTURE_CONSOLIDATED.md](./ARCHITECTURE_CONSOLIDATED.md) - System architecture and design
-- [FEATURES_AND_CAPABILITIES.md](./FEATURES_AND_CAPABILITIES.md) - Complete feature reference
-- [DEVELOPMENT_GUIDE.md](./DEVELOPMENT_GUIDE.md) - Setup and development workflow
-- [SECURITY_HARDENING.md](./SECURITY_HARDENING.md) - Security implementation details
-- [DATABASE_CONFIGURATION.md](./DATABASE_CONFIGURATION.md) - Database setup

**Progress & Planning:**
-- [PROJECT_PROGRESS.md](./PROJECT_PROGRESS.md) - Development progress and milestones
-- [TODOS.md](./TODOS.md) - Active backlog and technical debt

**Testing:**
-- [E2E_LOCAL_SETUP.md](./E2E_LOCAL_SETUP.md) - E2E testing setup

**Recent Features (Reference):**
-- [phase-10-person-identity.md](./phase-10-person-identity.md) - Person & Identity system
-- [phase-11-account-role-management.md](./phase-11-account-role-management.md) - Account management
-- [phase-12-admin-api-hr-integration.md](./phase-12-admin-api-hr-integration.md) - Future: API & HR integration

**Archives:**
- [docs/archive/](./archive/) - Historical documentation and completed phases
```

## 📊 Expected Results

**Before:** 44 files, 692KB
**After:** ~15-18 files, ~300-350KB in active docs + archives

**Final Structure:**
```
docs/
├── README.md (updated)
├── ARCHITECTURE_CONSOLIDATED.md (NEW - ~45KB)
├── FEATURES_AND_CAPABILITIES.md (NEW - ~35KB)
├── DEVELOPMENT_GUIDE.md (33KB)
├── DATABASE_CONFIGURATION.md (24KB)
├── SECURITY_HARDENING.md (9KB)
├── E2E_LOCAL_SETUP.md (updated - ~8KB)
├── PROJECT_PROGRESS.md (36KB)
├── TODOS.md (NEW - from backlog-and-debt.md)
├── phase-10-person-identity.md (31KB)
├── phase-11-account-role-management.md (24KB)
├── phase-12-admin-api-hr-integration.md (19KB)
├── notes-and-guidelines.md (0.5KB)
├── archive/
│   ├── phases/           (phase 1-9 docs, phase-11 prompts)
│   └── historical/       (PROJECT_STATUS.md, idp_req_details.md)
└── examples/             (keep as-is)
```

## 🔍 Preservation Checklist

**MUST preserve these key information pieces:**
1. ✅ System architecture diagrams and explanations
2. ✅ Database schema and migration guides
3. ✅ Security implementation details (CSP, cookies, headers)
4. ✅ Development setup instructions
5. ✅ E2E test setup and credentials
6. ✅ Active TODOs and technical debt
7. ✅ Recent major features (Phase 10, 11) for reference
8. ✅ Feature capabilities and API documentation
9. ✅ Authentication/authorization flows
10. ✅ Progress tracking and milestone history

**Safe to remove/archive:**
- ❌ Detailed implementation step-by-step prompts (completed work)
- ❌ Test failure reports (issues resolved)
- ❌ Test result logs (tests passing)
- ❌ Historical requirements documents
- ❌ Phase 1-9 detailed implementation docs (summarized in PROJECT_PROGRESS.md)
- ❌ Temporary/session-specific prompts
- ❌ Duplicate information across multiple files

## 🚀 Execution Instructions

1. **Read this entire prompt** to understand the strategy
2. **Create backup:** `git commit -m "docs: backup before refactoring"`
3. **Create archive directories:** `docs/archive/phases/` and `docs/archive/historical/`
4. **Start with consolidations** (Architecture, Features) - create new files first
5. **Move to archive** - relocate old phase docs and large historical files
6. **Delete obsolete files** - implementation prompts, test results
7. **Update README.md** - reflect new structure
8. **Rename backlog-and-debt.md → TODOS.md** and add Phase 12 items
9. **Extract TODOs from PROJECT_STATUS.md** before archiving
10. **Update PROJECT_PROGRESS.md** - remove excessive historical detail, keep only recent updates
11. **Commit:** `git commit -m "docs: refactor documentation structure - consolidate to 15 essential files"`

## ⚠️ Important Notes

- **Always preserve git history** - Use `git mv` for renames
- **Keep examples/ directory** - Contains useful code examples
- **Don't delete anything without archiving first** - Move to archive/ if uncertain
- **Test links** - Ensure all internal documentation links still work
- **Review PROJECT_PROGRESS.md** - It's 36KB and can be trimmed (keep only last 6 months of updates)

## ✨ Success Criteria

- ✅ File count reduced from 44 to ~15-18 active files
- ✅ Total size reduced by ~50% in active docs
- ✅ All critical information preserved
- ✅ Clear, discoverable documentation structure
- ✅ TODOS.md created with active backlog
- ✅ README.md updated with new structure
- ✅ No broken internal links
- ✅ Git history preserved for all files

---

**Start by creating the consolidated files (ARCHITECTURE_CONSOLIDATED.md and FEATURES_AND_CAPABILITIES.md) first, then proceed with archiving and deletion.**
