-- Phase 10.1: Data Migration - Create Person records for existing ApplicationUsers
-- This script creates a Person row for each existing ApplicationUser and links them together.
-- This is a one-time migration script that should be run after the Phase10_1_AddPersonEntity migration.

-- SQL Server Version

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

BEGIN TRY
    -- Create Person records from existing ApplicationUser data
    INSERT INTO Persons (
        Id,
        FirstName,
        MiddleName,
        LastName,
        Nickname,
        EmployeeId,
        Department,
        JobTitle,
        ProfileUrl,
        PictureUrl,
        Website,
        Address,
        Birthdate,
        Gender,
        TimeZone,
        Locale,
        CreatedBy,
        CreatedAt,
        ModifiedBy,
        ModifiedAt
    )
    SELECT 
        NEWID() as Id,  -- Generate new GUID for Person
        FirstName,
        MiddleName,
        LastName,
        Nickname,
        EmployeeId,
        Department,
        JobTitle,
        ProfileUrl,
        PictureUrl,
        Website,
        Address,
        Birthdate,
        Gender,
        TimeZone,
        Locale,
        CreatedBy,
        CreatedAt,
        NULL as ModifiedBy,  -- Not modified yet
        NULL as ModifiedAt
    FROM AspNetUsers
    WHERE PersonId IS NULL  -- Only process users without a Person link
    AND IsDeleted = 0;      -- Skip deleted users

    -- Link ApplicationUsers to their newly created Person records
    -- Match based on EmployeeId (if unique) or create individual persons
    WITH PersonMapping AS (
        SELECT 
            u.Id as UserId,
            p.Id as PersonId,
            ROW_NUMBER() OVER (PARTITION BY u.Id ORDER BY p.CreatedAt DESC) as RowNum
        FROM AspNetUsers u
        LEFT JOIN Persons p ON 
            (u.EmployeeId IS NOT NULL AND u.EmployeeId = p.EmployeeId)
            OR (
                u.EmployeeId IS NULL 
                AND u.FirstName = p.FirstName 
                AND u.LastName = p.LastName
                AND u.Birthdate = p.Birthdate
            )
        WHERE u.PersonId IS NULL
        AND u.IsDeleted = 0
    )
    UPDATE AspNetUsers
    SET PersonId = pm.PersonId
    FROM AspNetUsers u
    INNER JOIN PersonMapping pm ON u.Id = pm.UserId
    WHERE pm.RowNum = 1;

    -- Verify the migration
    DECLARE @UsersWithoutPerson INT;
    DECLARE @TotalActiveUsers INT;
    DECLARE @PersonsCreated INT;

    SELECT @UsersWithoutPerson = COUNT(*)
    FROM AspNetUsers
    WHERE PersonId IS NULL AND IsDeleted = 0;

    SELECT @TotalActiveUsers = COUNT(*)
    FROM AspNetUsers
    WHERE IsDeleted = 0;

    SELECT @PersonsCreated = COUNT(*)
    FROM Persons;

    PRINT 'Migration Summary:';
    PRINT '  Total active users: ' + CAST(@TotalActiveUsers AS VARCHAR);
    PRINT '  Users with Person link: ' + CAST((@TotalActiveUsers - @UsersWithoutPerson) AS VARCHAR);
    PRINT '  Users without Person link: ' + CAST(@UsersWithoutPerson AS VARCHAR);
    PRINT '  Total Persons created: ' + CAST(@PersonsCreated AS VARCHAR);

    IF @UsersWithoutPerson > 0
    BEGIN
        PRINT 'WARNING: Some users still without Person link. Manual review required.';
        -- Don't fail the transaction, just warn
    END

    COMMIT TRANSACTION;
    PRINT 'Migration completed successfully.';

END TRY
BEGIN CATCH
    ROLLBACK TRANSACTION;
    PRINT 'Migration failed. Error: ' + ERROR_MESSAGE();
    THROW;
END CATCH;
