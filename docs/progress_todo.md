# HybridIdP 待辦事項

> 📝 本文件列出所有待完成的 Phases 和功能

最後更新：2025-11-06

---

## 當前優先級

### 🎯 Next Up: Phase 5.5a - Settings Key/Value Store & Dynamic Branding

Phase 4.x 全部子階段已完成（詳見 `progress_completed.md`）。接下來專注於 Phase 5.5a，建立通用的設定服務與品牌動態化，為後續 Email/Security 設定鋪路。

本階段重點：

- [x] DB：新增 `Settings` entity 與 migration（Key 唯一、UpdatedUtc）✅ commit `feat(settings): Add Settings entity, SettingsService with caching, and BrandingService`
- [x] Service：`ISettingsService` + `SettingsService`（MemoryCache、快取失效）✅ commit `feat(settings): Add Settings entity, SettingsService with caching, and BrandingService`
- [x] Branding：讀取順序 DB > appsettings > 內建預設 ✅ commit `feat(settings): Integrate BrandingService in Razor views and add Settings API`
- [x] API：Admin 設定端點（讀取/更新/快取失效）✅ commit `feat(settings): Integrate BrandingService in Razor views and add Settings API`
- [x] UI：Admin Settings（先做 Branding，Email/Security 之後）✅ commit `feat(settings): Add Settings UI with branding configuration`
- [x] Tests：E2E via Playwright MCP - Settings CRUD, cache invalidation, branding display ✅ commit `fix(settings): Fix API to return array format and complete E2E testing`

**Phase 5.5a COMPLETE!** ✨ Settings Key/Value Store with dynamic branding fully working, tested end-to-end.

完成後再銜接 Phase 5.1–5.5 的安全策略工作。

---

<!-- Phase 4.x 已全部完成，移至 progress_completed.md 保存記錄 -->

---

### Phase 5.5a: Settings Key/Value Store & Dynamic Branding

---
 
---

<!-- Phase 4.7 已完成，詳細紀錄請見 progress_completed.md -->

---

---

### Phase 5.6: Consent Screen Management & API Resource Scopes

**目標：** 提供豐富的同意畫面自訂功能和 API 資源保護支援

#### Part 1: Consent Screen Customization

**Backend:**
- [ ] Add fields to `Scope` entity:
  - [ ] ConsentDisplayName (localized)
  - [ ] ConsentDescription (what permission allows)
  - [ ] IconUrl (optional icon)
  - [ ] IsRequired (cannot opt out)
  - [ ] DisplayOrder
- [ ] Create `Resources` table for localization
  - [ ] Support multiple languages (en-US, zh-TW)
- [ ] API: Update scope endpoints to include consent fields

**Frontend (Admin):**
- [ ] Enhance `ScopeForm.vue` with consent customization
- [ ] Multi-language editor for display name/description
- [ ] Icon upload/selection
- [ ] "Required" toggle
- [ ] Preview consent screen appearance

**Frontend (User-Facing):**
- [ ] Update `Consent.cshtml` with localized descriptions
- [ ] Group scopes by category (Profile, API Access, etc.)
- [ ] Show icons next to scopes
- [ ] Mark required scopes clearly

**驗證:**
- [ ] Admin can customize scope consent display
- [ ] Users see localized consent screen with clear descriptions
- [ ] Required scopes cannot be unchecked
- [ ] Scopes grouped by category

#### Part 2: API Resource Scopes

**Backend:**
- [ ] Create `ApiResource` entity
  - [ ] Name, DisplayName, Description, BaseUrl
  - [ ] Associated Scopes collection
- [ ] API: `GET /api/admin/resources`
- [ ] API: `POST /api/admin/resources`
- [ ] API: `PUT /api/admin/resources/{id}`
- [ ] API: `DELETE /api/admin/resources/{id}`
- [ ] API: `GET /api/admin/resources/{id}/scopes`
- [ ] OpenIddict integration (register resources, audience claim)

**Frontend:**
- [ ] Vue SPA: `ClientApp/src/admin/resources/ResourcesApp.vue`
- [ ] Create API resources (Company API, Inventory API, etc.)
- [ ] Assign scopes to resources
- [ ] Visual grouping in client configuration

**驗證:**
- [ ] Admin can create API resources
- [ ] Scopes can be assigned to resources
- [ ] Client configuration shows scopes grouped by resource
- [ ] Access tokens include audience claim

#### Part 3: Scope Authorization Policies (Whitelisting)

**Backend:**
- [ ] Manage `ClientAllowedScopes` (OpenIddict)
- [ ] Validation: Verify requested scopes against whitelist
- [ ] Update client APIs to manage allowed scopes

**Frontend:**
- [ ] Add "Allowed Scopes" multi-select in `ClientForm.vue`
- [ ] Group scopes by: Identity, API Resources, Custom
- [ ] Validation: `openid` required for OIDC clients

**驗證:**
- [ ] Client can only request whitelisted scopes
- [ ] Authorization denied for non-whitelisted scopes
- [ ] Scope selection grouped and easy to manage

**預計完成時間：** 3-4 開發 sessions

## Backlog (功能增強和技術債務)

### 功能增強

#### User Self-Service (Deferred for AD Integration)
- [ ] Implement user self-service password change flow
- [ ] Add password expiration check during login
- [ ] Prompt user to change password if expired
- [ ] Update user account management UI to show policy requirements

#### User Management
- [ ] Bulk user import (CSV)
- [ ] User profile picture upload
- [ ] Advanced user search (by department, role, creation date)
- [ ] User export (CSV/Excel)

#### Session Management
- [ ] Display active sessions (device, location, last active)
- [ ] Revoke session (logout from specific device)
- [ ] Revoke all sessions (logout everywhere)
- [ ] Suspicious login detection and alerts

#### Audit & Monitoring
- [ ] Advanced audit logging
- [ ] Audit log viewer with filters
- [ ] Export audit logs (CSV/Excel)
- [ ] Real-time activity dashboard
- [ ] Security alerts (failed login attempts, permission changes)

#### UI/UX Improvements
- [ ] Dark mode support
- [ ] Customizable admin dashboard
- [ ] Remember Me 功能改進
- [ ] Password strength indicator
- [ ] Keyboard shortcuts
- [ ] Accessibility improvements (WCAG 2.1 AA compliance)

#### API Improvements
- [ ] API documentation (Swagger UI 改進)
- [ ] API versioning
- [ ] Rate limiting per endpoint
- [ ] GraphQL support (optional)

### Security Hardening

**檢查清單：**
- [ ] HTTPS enforcement in production
- [ ] HSTS headers
- [ ] Rate limiting (login, API endpoints)
- [ ] Input validation comprehensive review
- [ ] SQL injection prevention audit
- [ ] XSS prevention audit
- [ ] CSRF protection verification
- [ ] Dependency vulnerability scanning
- [ ] Security headers review (X-Frame-Options, X-Content-Type-Options, etc.)

### Performance Optimization

**待優化：**
- [ ] Database indexing review and optimization
- [ ] Query optimization (N+1 problem check)
- [ ] API response caching strategy
- [ ] Frontend bundle optimization (Vite build analysis)
- [ ] Image optimization and lazy loading
- [ ] CDN configuration for static assets
- [ ] Database connection pooling tuning

### Testing

**測試涵蓋率提升：**
- [ ] Unit test coverage to 80%+ (currently ~60%)
- [ ] Integration tests for all API endpoints
- [ ] Frontend component unit tests (Vitest)
- [ ] Load testing (Apache JMeter / k6)
- [ ] Security testing (OWASP ZAP)
- [ ] Accessibility testing

### Technical Debt

**程式碼品質：**
- [ ] Refactor large controllers into smaller handlers/services
- [ ] Code style consistency (ESLint, Prettier)
- [ ] Dead code removal
- [ ] Magic number/string extraction to constants
- [ ] Comprehensive code comments and documentation

**Architecture:**
- [ ] Event-driven architecture for audit logging
- [ ] CQRS pattern for complex operations (optional)
- [ ] Domain events for loosely coupled features

### DevOps & Deployment

**CI/CD Pipeline:**
- [ ] GitHub Actions workflow for build/test
- [ ] Automated deployment to staging
- [ ] Automated deployment to production (with approval)
- [ ] Automated database migrations
- [ ] Rollback automation

**Containerization:**
- [ ] Multi-stage Docker build optimization
- [ ] Docker Compose for full stack (local development)
- [ ] Kubernetes deployment manifests (optional)
- [ ] Helm charts (optional)

**Monitoring & Observability:**
- [ ] Application Performance Monitoring (APM)
- [ ] Error tracking (Sentry / Application Insights)
- [ ] Metrics collection (Prometheus)
- [ ] Distributed tracing (Jaeger / Zipkin)
- [ ] Centralized logging (ELK stack / Seq)

**Database:**
- [ ] Database backup automation
- [ ] Database restore procedures
- [ ] Migration rollback strategy
- [ ] Database replication (read replicas)
- [ ] Database monitoring and alerting

---

## 注意事項

### ⚠️ 每個新功能必須：

1. **遵循 Small Steps Git 策略**
   - API → Tests → UI 分別 commit
   - 每個 endpoint/component 獨立 commit

2. **更新文件**
   - 完成後更新 `progress_completed.md`
   - 標記 `progress_todo.md` 完成項目
   - 必要時更新 `implementation_guidelines.md`

3. **測試**
   - Unit tests for services
   - API tests (Swagger UI 手動測試或 E2E)
   - E2E tests for critical flows (Playwright MCP)

4. **Tailwind CSS 設定**
   - 新 Vue SPA 必須建立 `style.css`
   - `main.js` 必須 `import './style.css'`

5. **Authorization 檢查**
   - Razor Page: `[Authorize(Roles = "Admin")]`
   - API Controller: `[Authorize(Roles = "Admin")]` or Permission-based

---

**下一步行動：** 開始 Phase 4.5 - Role Management UI

**參考：** `WORKFLOW.md` 查看詳細開發流程
