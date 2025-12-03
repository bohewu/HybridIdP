-- Setup Multi-Role Test User for E2E Testing
-- This script creates a test user with both Admin and User roles
-- Use this to test role switching functionality in E2E tests

-- User credentials:
-- Username: multitest@hybridauth.local
-- Password: MultiTest@123

SET QUOTED_IDENTIFIER ON;
GO

-- First, check if User role exists, if not create it
IF NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE NormalizedName = 'USER')
BEGIN
    DECLARE @UserRoleId UNIQUEIDENTIFIER = NEWID();
    INSERT INTO AspNetRoles (Id, Name, NormalizedName, Description, ConcurrencyStamp)
    VALUES (@UserRoleId, 'User', 'USER', 'Standard user role', NEWID());
    PRINT 'User role created';
END
ELSE
BEGIN
    PRINT 'User role already exists';
END
GO

-- Create multi-role test user if not exists
DECLARE @MultiRoleUserId UNIQUEIDENTIFIER;
DECLARE @AdminRoleId UNIQUEIDENTIFIER;
DECLARE @UserRoleId UNIQUEIDENTIFIER;

-- Get role IDs
SELECT @AdminRoleId = Id FROM AspNetRoles WHERE NormalizedName = 'ADMIN';
SELECT @UserRoleId = Id FROM AspNetRoles WHERE NormalizedName = 'USER';

-- Check if user already exists
IF NOT EXISTS (SELECT 1 FROM AspNetUsers WHERE NormalizedUserName = 'MULTITEST@HYBRIDAUTH.LOCAL')
BEGIN
    SET @MultiRoleUserId = NEWID();
    
    -- Create user
    -- Password: MultiTest@123
    -- This is the ASP.NET Core Identity V3 password hash for MultiTest@123
    INSERT INTO AspNetUsers (
        Id,
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
        IsActive
    )
    VALUES (
        @MultiRoleUserId,
        'multitest@hybridauth.local',
        'MULTITEST@HYBRIDAUTH.LOCAL',
        'multitest@hybridauth.local',
        'MULTITEST@HYBRIDAUTH.LOCAL',
        1, -- EmailConfirmed
        'AQAAAAIAAYagAAAAEKZvqCxZ5xPJG8Lf9kV7rJ1qO6Pv8wN9mX2Kl3Y4cZ5H6tR7sA8bU9vW1nE0fD2gI3jH', -- MultiTest@123 hashed
        NEWID(),
        NEWID(),
        0, -- PhoneNumberConfirmed
        0, -- TwoFactorEnabled
        1, -- LockoutEnabled
        0, -- AccessFailedCount
        GETUTCDATE(),
        1  -- IsActive
    );
    
    PRINT 'Multi-role test user created with ID: ' + CAST(@MultiRoleUserId AS VARCHAR(36));
END
ELSE
BEGIN
    SELECT @MultiRoleUserId = Id FROM AspNetUsers WHERE NormalizedUserName = 'MULTITEST@HYBRIDAUTH.LOCAL';
    PRINT 'Multi-role test user already exists with ID: ' + CAST(@MultiRoleUserId AS VARCHAR(36));
END

-- Assign Admin role if not already assigned
IF NOT EXISTS (SELECT 1 FROM AspNetUserRoles WHERE UserId = @MultiRoleUserId AND RoleId = @AdminRoleId)
BEGIN
    INSERT INTO AspNetUserRoles (UserId, RoleId)
    VALUES (@MultiRoleUserId, @AdminRoleId);
    PRINT 'Admin role assigned to multi-role test user';
END
ELSE
BEGIN
    PRINT 'Admin role already assigned';
END

-- Assign User role if not already assigned
IF NOT EXISTS (SELECT 1 FROM AspNetUserRoles WHERE UserId = @MultiRoleUserId AND RoleId = @UserRoleId)
BEGIN
    INSERT INTO AspNetUserRoles (UserId, RoleId)
    VALUES (@MultiRoleUserId, @UserRoleId);
    PRINT 'User role assigned to multi-role test user';
END
ELSE
BEGIN
    PRINT 'User role already assigned';
END

GO

PRINT '';
PRINT '=== Multi-Role Test User Setup Complete ===';
PRINT 'Username: multitest@hybridauth.local';
PRINT 'Password: MultiTest@123';
PRINT 'Roles: Admin, User';
PRINT '';
PRINT 'Use this user in E2E tests to verify role switching functionality.';
