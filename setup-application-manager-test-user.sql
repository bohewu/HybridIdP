-- Setup ApplicationManager Test User for E2E Testing
-- This script creates a test user with ApplicationManager role
-- Use this to test client/scope ownership functionality in E2E tests
--
-- NOTE: In development mode, the DataSeeder automatically creates this user.
--       This script is provided for manual setup or when using different environments.
--
-- User credentials:
-- Username: appmanager@hybridauth.local
-- Password: AppManager@123

SET QUOTED_IDENTIFIER ON;
GO

-- First, ensure ApplicationManager role exists with proper permissions
IF NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE NormalizedName = 'APPLICATIONMANAGER')
BEGIN
    DECLARE @AppManagerRoleId UNIQUEIDENTIFIER = NEWID();
    INSERT INTO AspNetRoles (Id, Name, NormalizedName, Description, Permissions, ConcurrencyStamp)
    VALUES (
        @AppManagerRoleId, 
        'ApplicationManager', 
        'APPLICATIONMANAGER', 
        'Can manage their own OAuth clients and scopes',
        'clients.read,clients.create,clients.update,clients.delete,scopes.read,scopes.create,scopes.update,scopes.delete',
        NEWID()
    );
    PRINT 'ApplicationManager role created with ID: ' + CAST(@AppManagerRoleId AS VARCHAR(36));
END
ELSE
BEGIN
    -- Update permissions if role exists but permissions might be outdated
    UPDATE AspNetRoles 
    SET Permissions = 'clients.read,clients.create,clients.update,clients.delete,scopes.read,scopes.create,scopes.update,scopes.delete',
        Description = 'Can manage their own OAuth clients and scopes'
    WHERE NormalizedName = 'APPLICATIONMANAGER';
    PRINT 'ApplicationManager role already exists, permissions updated';
END
GO

-- Create ApplicationManager test user if not exists
DECLARE @AppManagerUserId UNIQUEIDENTIFIER;
DECLARE @AppManagerRoleId UNIQUEIDENTIFIER;
DECLARE @PersonId UNIQUEIDENTIFIER;

-- Get ApplicationManager role ID
SELECT @AppManagerRoleId = Id FROM AspNetRoles WHERE NormalizedName = 'APPLICATIONMANAGER';

-- Check if user already exists
IF NOT EXISTS (SELECT 1 FROM AspNetUsers WHERE NormalizedUserName = 'APPMANAGER@HYBRIDAUTH.LOCAL')
BEGIN
    SET @AppManagerUserId = NEWID();
    SET @PersonId = NEWID();
    
    -- Create Person record first (required for ownership tracking)
    INSERT INTO Persons (
        Id,
        FirstName,
        LastName,
        Email,
        CreatedAt,
        IsDeleted
    )
    VALUES (
        @PersonId,
        'App',
        'Manager',
        'appmanager@hybridauth.local',
        GETUTCDATE(),
        0
    );
    PRINT 'Person record created with ID: ' + CAST(@PersonId AS VARCHAR(36));
    
    -- Create user with PersonId link
    -- Password: AppManager@123
    INSERT INTO AspNetUsers (
        Id,
        PersonId,
        UserName,
        NormalizedUserName,
        Email,
        NormalizedEmail,
        EmailConfirmed,
        PasswordHash,
        SecurityStamp,
        ConcurrencyStamp,
        PhoneNumberConfirmed,
        TwoFactorEnabled,
        LockoutEnabled,
        AccessFailedCount,
        CreatedAt,
        IsActive,
        FirstName,
        LastName
    )
    VALUES (
        @AppManagerUserId,
        @PersonId,
        'appmanager@hybridauth.local',
        'APPMANAGER@HYBRIDAUTH.LOCAL',
        'appmanager@hybridauth.local',
        'APPMANAGER@HYBRIDAUTH.LOCAL',
        1, -- EmailConfirmed
        'AQAAAAIAAYagAAAAEKZvqCxZ5xPJG8Lf9kV7rJ1qO6Pv8wN9mX2Kl3Y4cZ5H6tR7sA8bU9vW1nE0fD2gI3jH', -- Placeholder hash, will be reset
        NEWID(),
        NEWID(),
        0, -- PhoneNumberConfirmed
        0, -- TwoFactorEnabled
        1, -- LockoutEnabled
        0, -- AccessFailedCount
        GETUTCDATE(),
        1, -- IsActive
        'App',
        'Manager'
    );
    
    PRINT 'ApplicationManager test user created with ID: ' + CAST(@AppManagerUserId AS VARCHAR(36));
END
ELSE
BEGIN
    SELECT @AppManagerUserId = Id FROM AspNetUsers WHERE NormalizedUserName = 'APPMANAGER@HYBRIDAUTH.LOCAL';
    PRINT 'ApplicationManager test user already exists with ID: ' + CAST(@AppManagerUserId AS VARCHAR(36));
    
    -- Ensure PersonId is set
    IF NOT EXISTS (SELECT 1 FROM AspNetUsers WHERE Id = @AppManagerUserId AND PersonId IS NOT NULL)
    BEGIN
        SET @PersonId = NEWID();
        
        INSERT INTO Persons (Id, FirstName, LastName, Email, CreatedAt, IsDeleted)
        VALUES (@PersonId, 'App', 'Manager', 'appmanager@hybridauth.local', GETUTCDATE(), 0);
        
        UPDATE AspNetUsers SET PersonId = @PersonId WHERE Id = @AppManagerUserId;
        PRINT 'Person record created and linked to existing user';
    END
END

-- Assign ApplicationManager role if not already assigned
IF NOT EXISTS (SELECT 1 FROM AspNetUserRoles WHERE UserId = @AppManagerUserId AND RoleId = @AppManagerRoleId)
BEGIN
    INSERT INTO AspNetUserRoles (UserId, RoleId)
    VALUES (@AppManagerUserId, @AppManagerRoleId);
    PRINT 'ApplicationManager role assigned to test user';
END
ELSE
BEGIN
    PRINT 'ApplicationManager role already assigned';
END

-- Verify setup
PRINT '';
PRINT '=== ApplicationManager Test User Setup Complete ===';
PRINT 'Username: appmanager@hybridauth.local';
PRINT 'Password: AppManager@123 (needs to be reset via UI or API)';
PRINT '';
SELECT 
    u.Id as UserId,
    u.UserName,
    u.Email,
    u.PersonId,
    p.FirstName,
    p.LastName,
    r.Name as RoleName,
    r.Permissions
FROM AspNetUsers u
LEFT JOIN Persons p ON u.PersonId = p.Id
LEFT JOIN AspNetUserRoles ur ON u.Id = ur.UserId
LEFT JOIN AspNetRoles r ON ur.RoleId = r.Id
WHERE u.NormalizedUserName = 'APPMANAGER@HYBRIDAUTH.LOCAL';
GO
