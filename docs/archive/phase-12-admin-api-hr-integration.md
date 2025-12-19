# Phase 12: Admin API for HR Integration

## Overview

Phase 12 implements a comprehensive Admin API for external HR systems to manage user accounts, roles, and person identities programmatically. This enables seamless integration with institutional HR databases through OAuth 2.0 Client Credentials flow.

## Architecture Principles

### 1. **Separation of Concerns**
- IdP Platform: Authentication, Authorization, Identity Management
- HR System: Source of truth for employee data, organizational structure
- Integration Pattern: External HR system calls IdP APIs (not embedded)

### 2. **Security Model**
- OAuth 2.0 Client Credentials Grant for machine-to-machine communication
- API requires `admin` scope for all operations
- Dedicated service accounts with limited permissions
- Audit logging for all administrative actions
- IP whitelisting and rate limiting (optional)

### 3. **Data Synchronization Strategy**
- **Push Model**: HR system pushes changes to IdP when events occur
- **Reconciliation**: Periodic full sync to handle missed events
- **Conflict Resolution**: HR system is authoritative for Person data
- **Idempotency**: All operations support safe retries

## API Design

### Base URL
```
https://idp.example.com/api/admin/v1
```

### Authentication
All requests require Bearer token obtained via OAuth 2.0 Client Credentials flow:

```http
POST /connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials
&client_id={hr_system_client_id}
&client_secret={hr_system_client_secret}
&scope=admin
```

Response:
```json
{
  "access_token": "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9...",
  "token_type": "Bearer",
  "expires_in": 3600,
  "scope": "admin"
}
```

### API Endpoints

#### 1. Person Management

##### 1.1 Create or Update Person
**Purpose**: Synchronize employee data from HR system to IdP

```http
PUT /api/admin/v1/persons/{employeeId}
Authorization: Bearer {access_token}
Content-Type: application/json

{
  "employeeId": "E123456",
  "nationalId": "A123456789",
  "chineseName": "王小明",
  "englishName": "Wang, Hsiao-Ming",
  "email": "wang.xm@university.edu",
  "department": "資訊工程學系",
  "title": "副教授",
  "phone": "02-12345678",
  "isActive": true,
  "effectiveDate": "2024-02-01T00:00:00Z"
}
```

**Response 200 OK:**
```json
{
  "personId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "employeeId": "E123456",
  "created": false,
  "updated": true,
  "changes": ["department", "title"]
}
```

**Business Rules:**
- Creates new Person if `employeeId` doesn't exist
- Updates existing Person if found
- Soft-deletes Person if `isActive: false`
- Does NOT create user accounts (see User Management)

##### 1.2 Get Person by Employee ID
```http
GET /api/admin/v1/persons/{employeeId}
Authorization: Bearer {access_token}
```

**Response 200 OK:**
```json
{
  "personId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "employeeId": "E123456",
  "nationalId": "A123456789",
  "chineseName": "王小明",
  "englishName": "Wang, Hsiao-Ming",
  "email": "wang.xm@university.edu",
  "department": "資訊工程學系",
  "title": "副教授",
  "userAccounts": [
    {
      "userId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
      "username": "wang.xm",
      "email": "wang.xm@university.edu",
      "roles": ["Faculty", "Admin"],
      "isActive": true
    }
  ],
  "isActive": true,
  "createdUtc": "2024-01-01T00:00:00Z",
  "updatedUtc": "2024-02-01T10:30:00Z"
}
```

##### 1.3 Batch Sync Persons
**Purpose**: Full reconciliation of all employees

```http
POST /api/admin/v1/persons/batch-sync
Authorization: Bearer {access_token}
Content-Type: application/json

{
  "persons": [
    { "employeeId": "E123456", "chineseName": "王小明", ... },
    { "employeeId": "E123457", "chineseName": "李小華", ... }
  ],
  "syncTimestamp": "2024-12-01T00:00:00Z",
  "deactivateMissing": false
}
```

**Response 200 OK:**
```json
{
  "totalProcessed": 150,
  "created": 5,
  "updated": 12,
  "deactivated": 3,
  "errors": [
    {
      "employeeId": "E999999",
      "error": "Invalid national ID format"
    }
  ]
}
```

#### 2. User Account Management

##### 2.1 Create User Account
**Purpose**: Create login account for a Person

```http
POST /api/admin/v1/users
Authorization: Bearer {access_token}
Content-Type: application/json

{
  "personId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "username": "wang.xm",
  "email": "wang.xm@university.edu",
  "password": "TempP@ssw0rd!",
  "requirePasswordChange": true,
  "roles": ["Faculty"]
}
```

**Response 201 Created:**
```json
{
  "userId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "username": "wang.xm",
  "personId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "temporaryPassword": "TempP@ssw0rd!",
  "passwordResetUrl": "https://idp.example.com/account/reset-password?token=..."
}
```

**Business Rules:**
- Person must exist before creating user account
- One Person can have multiple user accounts (e.g., faculty + student)
- Password must meet complexity requirements
- Account activation email sent automatically

##### 2.2 Update User Account
```http
PUT /api/admin/v1/users/{userId}
Authorization: Bearer {access_token}
Content-Type: application/json

{
  "email": "wang.new@university.edu",
  "isActive": true,
  "roles": ["Faculty", "Admin"]
}
```

**Response 200 OK:**
```json
{
  "userId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "updated": true,
  "changes": ["email", "roles"]
}
```

##### 2.3 Deactivate User Account
```http
DELETE /api/admin/v1/users/{userId}
Authorization: Bearer {access_token}
```

**Response 200 OK:**
```json
{
  "userId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "deactivated": true,
  "sessionsTerminated": 3
}
```

**Business Rules:**
- Soft delete (sets IsDeleted = true)
- Terminates all active sessions
- Preserves audit history
- Can be restored by setting IsDeleted = false

##### 2.4 Reset User Password
```http
POST /api/admin/v1/users/{userId}/reset-password
Authorization: Bearer {access_token}
Content-Type: application/json

{
  "notifyUser": true,
  "requirePasswordChange": true
}
```

**Response 200 OK:**
```json
{
  "userId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "temporaryPassword": "Auto-Gen-P@ss123",
  "passwordResetUrl": "https://idp.example.com/account/reset-password?token=...",
  "emailSent": true
}
```

#### 3. Role Management

##### 3.1 Assign Role to User
```http
POST /api/admin/v1/users/{userId}/roles
Authorization: Bearer {access_token}
Content-Type: application/json

{
  "roleId": "a1b2c3d4-5678-90ab-cdef-1234567890ab",
  "effectiveDate": "2024-02-01T00:00:00Z",
  "expirationDate": null,
  "reason": "Promoted to department chair"
}
```

**Response 200 OK:**
```json
{
  "userId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "roleId": "a1b2c3d4-5678-90ab-cdef-1234567890ab",
  "roleName": "DepartmentChair",
  "assigned": true
}
```

##### 3.2 Remove Role from User
```http
DELETE /api/admin/v1/users/{userId}/roles/{roleId}
Authorization: Bearer {access_token}
```

**Response 200 OK:**
```json
{
  "userId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "roleId": "a1b2c3d4-5678-90ab-cdef-1234567890ab",
  "removed": true,
  "sessionsInvalidated": 2
}
```

**Business Rules:**
- Phase 11 active role: If removed role was active in any session, those sessions are terminated
- Cannot remove last role from user
- Audit log records who removed role and reason

##### 3.3 List Available Roles
```http
GET /api/admin/v1/roles
Authorization: Bearer {access_token}
```

**Response 200 OK:**
```json
{
  "roles": [
    {
      "roleId": "a1b2c3d4-5678-90ab-cdef-1234567890ab",
      "name": "Admin",
      "description": "System administrators with full access",
      "permissions": ["admin.users", "admin.roles", "admin.settings"],
      "requiresPasswordConfirmation": true,
      "isSystemRole": true
    },
    {
      "roleId": "b2c3d4e5-6789-01bc-def1-234567890abc",
      "name": "Faculty",
      "description": "Teaching faculty members",
      "permissions": ["courses.manage", "grades.submit"],
      "requiresPasswordConfirmation": false,
      "isSystemRole": false
    }
  ]
}
```

#### 4. Query & Reporting

##### 4.1 Search Users
```http
GET /api/admin/v1/users/search?query=wang&includeInactive=false&page=1&pageSize=20
Authorization: Bearer {access_token}
```

**Response 200 OK:**
```json
{
  "items": [
    {
      "userId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
      "username": "wang.xm",
      "email": "wang.xm@university.edu",
      "personId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "employeeId": "E123456",
      "chineseName": "王小明",
      "roles": ["Faculty", "Admin"],
      "isActive": true,
      "lastLoginUtc": "2024-12-01T08:30:00Z"
    }
  ],
  "totalCount": 1,
  "page": 1,
  "pageSize": 20
}
```

##### 4.2 Get User Audit History
```http
GET /api/admin/v1/users/{userId}/audit-history?days=30
Authorization: Bearer {access_token}
```

**Response 200 OK:**
```json
{
  "userId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "events": [
    {
      "eventId": 12345,
      "eventType": "RoleAssigned",
      "timestamp": "2024-11-15T10:00:00Z",
      "performedBy": "hr-system-client",
      "details": "Assigned role: DepartmentChair",
      "ipAddress": "10.0.1.5"
    },
    {
      "eventId": 12344,
      "eventType": "UserLogin",
      "timestamp": "2024-12-01T08:30:00Z",
      "details": "Successful login from browser",
      "ipAddress": "140.113.1.100"
    }
  ]
}
```

## Implementation Plan

### Phase 12.1: OAuth Client Setup
**Goal**: Configure HR system as OAuth client

**Tasks:**
1. Create `HRSystemClient` in OpenIddict Clients table
2. Configure Client Credentials flow with admin scope
3. Add IP whitelisting middleware (optional)
4. Document client registration process

**Deliverables:**
- Migration to add HR client
- Client configuration documentation
- Token validation tests

### Phase 12.2: Person Management API
**Goal**: Implement CRUD for Person entities

**Tasks:**
1. Create `IPersonAdminService` interface
2. Implement `PersonAdminService` with:
   - `CreateOrUpdatePersonAsync`
   - `GetPersonByEmployeeIdAsync`
   - `BatchSyncPersonsAsync`
3. Create `PersonAdminController`
4. Add DTO models for requests/responses
5. Unit tests (20+ tests)

**Deliverables:**
- Person admin service with tests
- API endpoints for Person CRUD
- Swagger documentation

### Phase 12.3: User Account Management API
**Goal**: Implement user lifecycle operations

**Tasks:**
1. Create `IUserAdminService` interface
2. Implement `UserAdminService` with:
   - `CreateUserAccountAsync`
   - `UpdateUserAccountAsync`
   - `DeactivateUserAccountAsync`
   - `ResetPasswordAsync`
3. Create `UserAdminController`
4. Add password generation utility
5. Email notification for new accounts
6. Unit tests (25+ tests)

**Deliverables:**
- User admin service with tests
- API endpoints for user management
- Password reset email template

### Phase 12.4: Role Management API
**Goal**: Programmatic role assignment

**Tasks:**
1. Create `IRoleAdminService` interface
2. Implement `RoleAdminService` with:
   - `AssignRoleToUserAsync`
   - `RemoveRoleFromUserAsync`
   - `ListAvailableRolesAsync`
3. Create `RoleAdminController`
4. Integration with Phase 11 active role system
5. Session invalidation on role removal
6. Unit tests (15+ tests)

**Deliverables:**
- Role admin service with tests
- API endpoints for role operations
- Session cleanup logic

### Phase 12.5: Query & Reporting API
**Goal**: Enable HR system to query IdP state

**Tasks:**
1. Implement `SearchUsersAsync` with pagination
2. Implement `GetUserAuditHistoryAsync`
3. Add filtering and sorting capabilities
4. Performance optimization with indexes
5. Integration tests

**Deliverables:**
- Search and reporting endpoints
- Database indexes for performance
- Integration tests

### Phase 12.6: HR Integration Example
**Goal**: Provide reference implementation

**Tasks:**
1. Create sample HR connector in C# (.NET)
2. Implement sync logic:
   - Incremental sync (push on HR events)
   - Full reconciliation (periodic batch)
   - Error handling and retry
3. Configuration documentation
4. Deployment guide

**Deliverables:**
- Sample HR connector code
- Integration guide document
- Docker compose setup for testing

## Security Considerations

### 1. **OAuth Scopes**
```
admin              - Full administrative access (Person, User, Role)
admin.users.read   - Read-only user data
admin.users.write  - Create/update/delete users
admin.roles        - Manage role assignments
```

### 2. **Permission Authorization**
Admin API requires special permission:
```
"permissions": ["admin.api.access"]
```

Only Admin role has this by default. HR client uses service account with admin role.

### 3. **Rate Limiting**
```csharp
// appsettings.json
"AdminApiRateLimiting": {
  "EnableRateLimiting": true,
  "PermitLimit": 100,
  "Window": "00:01:00",  // 1 minute
  "QueueLimit": 10
}
```

### 4. **IP Whitelisting**
```csharp
// appsettings.json
"AdminApiSecurity": {
  "EnableIpWhitelist": true,
  "AllowedIPs": [
    "10.0.1.0/24",      // HR system subnet
    "140.113.1.5"       // Backup sync server
  ]
}
```

### 5. **Audit Logging**
All admin API calls logged with:
- Client ID (which system)
- Action performed
- Target user/person
- Timestamp
- IP address
- Result (success/failure)

## Error Handling

### Standard Error Response
```json
{
  "error": "InvalidRequest",
  "errorDescription": "Person with employeeId 'E999999' not found",
  "errorCode": "PERSON_NOT_FOUND",
  "timestamp": "2024-12-01T10:30:00Z",
  "traceId": "0HMVFE00ABCD-0000001"
}
```

### Error Codes
| Code | Description | HTTP Status |
|------|-------------|-------------|
| `PERSON_NOT_FOUND` | Person doesn't exist | 404 |
| `USER_NOT_FOUND` | User account doesn't exist | 404 |
| `INVALID_EMPLOYEE_ID` | Employee ID format invalid | 400 |
| `DUPLICATE_USERNAME` | Username already taken | 409 |
| `ROLE_NOT_FOUND` | Role doesn't exist | 404 |
| `INVALID_PASSWORD` | Password doesn't meet policy | 400 |
| `PERMISSION_DENIED` | Client lacks required scope | 403 |
| `RATE_LIMIT_EXCEEDED` | Too many requests | 429 |

## Testing Strategy

### Unit Tests
- Service layer logic (60+ tests)
- Business rule validation
- Error handling scenarios

### Integration Tests
- Full API request/response flow
- OAuth token validation
- Database transactions
- Session cleanup

### E2E Tests (Playwright)
- HR system connects and authenticates
- Creates new person and user account
- Assigns role to user
- User logs in with new account
- Performs role switch

### Performance Tests
- Batch sync of 1000 persons
- Concurrent API requests
- Database query optimization

## Monitoring & Observability

### Metrics to Track
- API request rate (requests/min)
- API response time (p50, p95, p99)
- Error rate by endpoint
- OAuth token issuance rate
- Failed authentication attempts

### Alerts
- High error rate (> 5%)
- Slow response time (> 2s)
- Failed HR sync
- Unauthorized access attempts

### Logs
```
[2024-12-01 10:30:00] [INFO] AdminAPI: HR client authenticated (client_id=hr-system-prod)
[2024-12-01 10:30:01] [INFO] PersonAdminService: Created person E123456 (王小明)
[2024-12-01 10:30:02] [INFO] UserAdminService: Created user wang.xm for person E123456
[2024-12-01 10:30:03] [INFO] RoleAdminService: Assigned Faculty role to wang.xm
```

## Migration & Deployment

### Database Changes
- No schema changes required (uses existing Person/User/Role tables)
- Add indexes for search performance

### Configuration
```json
// appsettings.Production.json
{
  "AdminApi": {
    "Enabled": true,
    "BaseUrl": "https://idp.example.com/api/admin/v1",
    "RequireHttps": true
  },
  "HRIntegration": {
    "ClientId": "hr-system-prod",
    "AllowedIPRanges": ["10.0.1.0/24"]
  }
}
```

### Deployment Steps
1. Deploy IdP with Admin API enabled
2. Register HR client credentials
3. Configure IP whitelist
4. Deploy HR connector to sync server
5. Run initial full reconciliation
6. Enable incremental sync

## Documentation Deliverables

1. **API Reference** (Swagger/OpenAPI)
2. **Integration Guide** for HR system developers
3. **Security Best Practices**
4. **Troubleshooting Guide**
5. **Sample Code** (C#, Python, PowerShell)

## Future Enhancements (Phase 13+)

### Webhook Notifications
IdP pushes events to HR system:
- User account created
- Role changed
- Account deactivated

### Advanced Sync Strategies
- Delta sync with change tracking
- Conflict resolution policies
- Manual review queue for conflicts

### SCIM 2.0 Support
Standard protocol for user provisioning:
```http
POST /scim/v2/Users
GET /scim/v2/Users/{id}
PUT /scim/v2/Users/{id}
DELETE /scim/v2/Users/{id}
```

### GraphQL API
Alternative to REST for complex queries:
```graphql
query GetUserDetails($employeeId: String!) {
  person(employeeId: $employeeId) {
    chineseName
    department
    userAccounts {
      username
      roles {
        name
        permissions
      }
    }
  }
}
```

## Success Criteria

✅ HR system can create/update persons programmatically  
✅ HR system can create user accounts with temporary passwords  
✅ HR system can assign/remove roles  
✅ All operations are audited  
✅ API is secured with OAuth Client Credentials  
✅ 95% test coverage on admin services  
✅ API response time < 500ms (p95)  
✅ Zero downtime during HR sync operations  
✅ Documentation complete and reviewed by HR team

## Timeline Estimate

| Phase | Duration | Dependencies |
|-------|----------|--------------|
| 12.1 OAuth Setup | 2 days | None |
| 12.2 Person API | 3 days | 12.1 |
| 12.3 User API | 4 days | 12.2 |
| 12.4 Role API | 3 days | Phase 11 complete |
| 12.5 Query API | 2 days | 12.2, 12.3 |
| 12.6 HR Example | 3 days | All above |
| **Total** | **17 days** | |

## References

- [OAuth 2.0 Client Credentials Grant](https://datatracker.ietf.org/doc/html/rfc6749#section-4.4)
- [OpenIddict Documentation](https://documentation.openiddict.com/)
- [ASP.NET Core Rate Limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit)
- [SCIM 2.0 Specification](https://datatracker.ietf.org/doc/html/rfc7644)
