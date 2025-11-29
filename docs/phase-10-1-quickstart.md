# Phase 10.1: Person Entity - Quick Start Guide

## Overview

Phase 10.1 introduces the `Person` entity to support multi-account identity management. A single Person can have multiple ApplicationUser accounts (e.g., contractor account + permanent employee account).

## What Was Added

### Database Schema
- **New Table**: `Persons` with profile and employment information
- **New Column**: `ApplicationUser.PersonId` (nullable FK to Persons)
- **Index**: Unique index on `EmployeeId` (filtered for non-null values)
- **Relationship**: Person ↔ ApplicationUser (one-to-many, OnDelete: SetNull)

### Code Files
- `Core.Domain.Entities.Person` - Person entity
- `ApplicationUser.PersonId` - Link to Person
- Updated DbContext and migrations for SQL Server & PostgreSQL

## How to Apply Migration

### Option 1: Using the Automation Script (Recommended)

**For SQL Server:**
```powershell
.\scripts\run-phase10-1-migration.ps1
```

**For PostgreSQL:**
```powershell
.\scripts\run-phase10-1-migration.ps1 -DatabaseProvider PostgreSQL
```

**Skip data backfill (only schema):**
```powershell
.\scripts\run-phase10-1-migration.ps1 -SkipBackfill
```

### Option 2: Manual Steps

#### 1. Apply Database Migration

**SQL Server:**
```powershell
dotnet ef database update --project Infrastructure.Migrations.SqlServer --startup-project Web.IdP --context ApplicationDbContext
```

**PostgreSQL:**
```powershell
$env:DatabaseProvider='PostgreSQL'
dotnet ef database update --project Infrastructure.Migrations.Postgres --startup-project Web.IdP --context ApplicationDbContext
$env:DatabaseProvider=$null
```

#### 2. Run Data Backfill Script

**SQL Server:**
```powershell
sqlcmd -S localhost,1433 -U sa -P "YourStrong!Passw0rd" -d hybridauth_idp -i .\scripts\phase10-1-backfill-persons-sqlserver.sql -C
```

**PostgreSQL:**
```bash
psql -h localhost -p 5432 -d hybridauth_idp -U user -f ./scripts/phase10-1-backfill-persons-postgres.sql
```

## Verification

### 1. Check Database Schema

**SQL Server:**
```sql
-- Check Persons table exists
SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Persons';

-- Check PersonId column in AspNetUsers
SELECT * FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_NAME = 'AspNetUsers' AND COLUMN_NAME = 'PersonId';
```

**PostgreSQL:**
```sql
-- Check Persons table exists
SELECT * FROM information_schema.tables WHERE table_name = 'Persons';

-- Check PersonId column in AspNetUsers
SELECT * FROM information_schema.columns 
WHERE table_name = 'AspNetUsers' AND column_name = 'PersonId';
```

### 2. Run Unit Tests

```powershell
dotnet test --filter "FullyQualifiedName~PersonEntityTests"
```

Expected: **9/9 tests passing**

### 3. Check Data Migration

**SQL Server:**
```sql
-- Count users and persons
SELECT 
    (SELECT COUNT(*) FROM AspNetUsers WHERE IsDeleted = 0) as TotalUsers,
    (SELECT COUNT(*) FROM AspNetUsers WHERE PersonId IS NOT NULL) as UsersWithPerson,
    (SELECT COUNT(*) FROM Persons) as TotalPersons;
```

**PostgreSQL:**
```sql
-- Count users and persons
SELECT 
    (SELECT COUNT(*) FROM "AspNetUsers" WHERE "IsDeleted" = false) as TotalUsers,
    (SELECT COUNT(*) FROM "AspNetUsers" WHERE "PersonId" IS NOT NULL) as UsersWithPerson,
    (SELECT COUNT(*) FROM "Persons") as TotalPersons;
```

## Example Usage

### Create a Person with Multiple Accounts

```csharp
// Create a Person
var person = new Person
{
    Id = Guid.NewGuid(),
    FirstName = "John",
    LastName = "Doe",
    EmployeeId = "EMP001",
    Department = "Engineering",
    JobTitle = "Senior Developer",
    CreatedAt = DateTime.UtcNow
};

// Create two accounts for the same person
var contractAccount = new ApplicationUser
{
    Id = Guid.NewGuid(),
    UserName = "john.doe.contract",
    Email = "john.contract@company.com",
    PersonId = person.Id
};

var permanentAccount = new ApplicationUser
{
    Id = Guid.NewGuid(),
    UserName = "john.doe",
    Email = "john@company.com",
    PersonId = person.Id
};

// Both accounts share the same Person profile
```

### Query Accounts by Person

```csharp
// Get all accounts for a person
var person = await context.Persons
    .Include(p => p.Accounts)
    .FirstOrDefaultAsync(p => p.EmployeeId == "EMP001");

foreach (var account in person.Accounts)
{
    Console.WriteLine($"Account: {account.UserName}");
}
```

## Backward Compatibility

- ✅ **PersonId is nullable**: Existing code works without changes
- ✅ **Profile fields kept in ApplicationUser**: No breaking changes to existing APIs
- ✅ **Gradual migration**: Can link users to persons over time

## Rollback Procedure

If you need to rollback the migration:

**SQL Server:**
```powershell
dotnet ef database update 20251126141202_AddClientRequiredScope --project Infrastructure.Migrations.SqlServer --startup-project Web.IdP --context ApplicationDbContext
```

**PostgreSQL:**
```powershell
$env:DatabaseProvider='PostgreSQL'
dotnet ef database update 20251126141239_AddClientRequiredScope --project Infrastructure.Migrations.Postgres --startup-project Web.IdP --context ApplicationDbContext
$env:DatabaseProvider=$null
```

This will:
1. Drop the `Persons` table
2. Remove `PersonId` column from `AspNetUsers`
3. Remove foreign key and indexes

## Troubleshooting

### Migration fails with "PendingModelChangesWarning"

This is expected and safe. The warning is ignored in `Program.cs` configuration. The migration will still apply correctly.

### Backfill script reports "users without Person link"

This is expected if:
- Users were soft-deleted (`IsDeleted = true`)
- New users were created after the migration but before backfill

Run the backfill script again to link new users, or link them manually via Phase 10.2 API (coming next).

### PostgreSQL: "relation does not exist"

Ensure you're using quoted identifiers in queries:
- Use `"Persons"` instead of `Persons`
- Use `"AspNetUsers"` instead of `AspNetUsers`

### SQL Server: Unique constraint violation on EmployeeId

This means you have duplicate EmployeeIds in your data. The filtered unique index only applies to non-null values, so null EmployeeIds are allowed.

To fix:
1. Find duplicates: `SELECT EmployeeId, COUNT(*) FROM AspNetUsers GROUP BY EmployeeId HAVING COUNT(*) > 1`
2. Update duplicates to make them unique
3. Re-run the migration

## Next Steps

After Phase 10.1 is complete:

1. **Phase 10.2**: Implement `IPersonService` and Person management APIs
2. **Phase 10.3**: Add Person management UI in Admin panel
3. **Phase 10.4**: Migrate profile fields from ApplicationUser to Person (optional cleanup)

## Support

For issues or questions:
- Check `docs/phase-10-person-identity.md` for detailed design
- Review unit tests in `Tests.Infrastructure.UnitTests/PersonEntityTests.cs`
- See `docs/DATABASE_CONFIGURATION.md` for database management

---

**Last Updated**: 2025-11-29  
**Phase**: 10.1 Complete ✅  
**Next Phase**: 10.2 (Services & API)
