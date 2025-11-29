-- Phase 10.4: Sync ApplicationUser profile data to linked Person records (SQL Server)
-- This script copies profile fields from ApplicationUser to their linked Person entity
-- Run after Phase 10.1 backfill (ensures all users have PersonId populated)

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @UpdatedCount INT = 0;
DECLARE @SkippedCount INT = 0;

PRINT 'Starting Phase 10.4 profile synchronization...';
PRINT '';

BEGIN TRY
    BEGIN TRANSACTION;

    -- Update Person records with ApplicationUser profile data
    -- Only updates where ApplicationUser has non-null values
    UPDATE p
    SET 
        p.FirstName = COALESCE(u.FirstName, p.FirstName),
        p.MiddleName = COALESCE(u.MiddleName, p.MiddleName),
        p.LastName = COALESCE(u.LastName, p.LastName),
        p.Nickname = COALESCE(u.Nickname, p.Nickname),
        p.EmployeeId = COALESCE(u.EmployeeId, p.EmployeeId),
        p.Department = COALESCE(u.Department, p.Department),
        p.JobTitle = COALESCE(u.JobTitle, p.JobTitle),
        p.ProfileUrl = COALESCE(u.ProfileUrl, p.ProfileUrl),
        p.PictureUrl = COALESCE(u.PictureUrl, p.PictureUrl),
        p.Website = COALESCE(u.Website, p.Website),
        p.Address = COALESCE(u.Address, p.Address),
        p.Birthdate = COALESCE(u.Birthdate, p.Birthdate),
        p.Gender = COALESCE(u.Gender, p.Gender),
        p.TimeZone = COALESCE(u.TimeZone, p.TimeZone),
        p.Locale = COALESCE(u.Locale, p.Locale),
        p.ModifiedAt = GETUTCDATE(),
        p.ModifiedBy = NULL  -- System migration, no specific user
    FROM Persons p
    INNER JOIN AspNetUsers u ON u.PersonId = p.Id
    WHERE 
        -- Only update if ApplicationUser has non-null values (simplified to avoid data type comparison issues)
        u.FirstName IS NOT NULL
        OR u.MiddleName IS NOT NULL
        OR u.LastName IS NOT NULL
        OR u.Nickname IS NOT NULL
        OR u.EmployeeId IS NOT NULL
        OR u.Department IS NOT NULL
        OR u.JobTitle IS NOT NULL
        OR u.ProfileUrl IS NOT NULL
        OR u.PictureUrl IS NOT NULL
        OR u.Website IS NOT NULL
        OR u.Address IS NOT NULL
        OR u.Birthdate IS NOT NULL
        OR u.Gender IS NOT NULL
        OR u.TimeZone IS NOT NULL
        OR u.Locale IS NOT NULL;

    SET @UpdatedCount = @@ROWCOUNT;

    -- Count users with no PersonId (should be 0 after Phase 10.1)
    SELECT @SkippedCount = COUNT(*)
    FROM AspNetUsers
    WHERE PersonId IS NULL;

    COMMIT TRANSACTION;

    -- Display summary
    PRINT 'Profile synchronization completed successfully!';
    PRINT '';
    PRINT 'Summary:';
    PRINT '  Person records updated: ' + CAST(@UpdatedCount AS NVARCHAR(10));
    PRINT '  Users without PersonId: ' + CAST(@SkippedCount AS NVARCHAR(10));
    
    IF @SkippedCount > 0
    BEGIN
        PRINT '';
        PRINT 'WARNING: Some users do not have PersonId assigned!';
        PRINT 'Run Phase 10.1 backfill script first.';
        
        SELECT Id, Email, UserName
        FROM AspNetUsers
        WHERE PersonId IS NULL;
    END

    -- Show sample of updated Person records
    PRINT '';
    PRINT 'Sample of updated Person records:';
    SELECT TOP 5
        p.Id,
        p.FirstName,
        p.LastName,
        p.EmployeeId,
        p.Department,
        u.Email AS LinkedUserEmail
    FROM Persons p
    INNER JOIN AspNetUsers u ON u.PersonId = p.Id
    ORDER BY p.ModifiedAt DESC;

END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    
    PRINT 'Error occurred during profile synchronization:';
    PRINT ERROR_MESSAGE();
    THROW;
END CATCH;
