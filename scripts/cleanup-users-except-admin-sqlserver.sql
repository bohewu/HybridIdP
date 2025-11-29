-- Cleanup all users except admin@hybridauth.local
-- Run this script to remove test data before Phase 10.4 migration

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @AdminEmail NVARCHAR(256) = 'admin@hybridauth.local';
DECLARE @DeletedCount INT = 0;

BEGIN TRY
    BEGIN TRANSACTION;

    -- Step 1: Delete user roles for non-admin users
    DELETE FROM AspNetUserRoles 
    WHERE UserId IN (
        SELECT Id FROM AspNetUsers 
        WHERE Email != @AdminEmail
    );
    
    PRINT 'User roles deleted successfully.';

    -- Step 2: Delete user claims for non-admin users
    DELETE FROM AspNetUserClaims
    WHERE UserId IN (
        SELECT Id FROM AspNetUsers 
        WHERE Email != @AdminEmail
    );
    
    PRINT 'User claims deleted successfully.';

    -- Step 3: Delete user logins for non-admin users
    DELETE FROM AspNetUserLogins
    WHERE UserId IN (
        SELECT Id FROM AspNetUsers 
        WHERE Email != @AdminEmail
    );
    
    PRINT 'User logins deleted successfully.';

    -- Step 4: Delete user tokens for non-admin users
    DELETE FROM AspNetUserTokens
    WHERE UserId IN (
        SELECT Id FROM AspNetUsers 
        WHERE Email != @AdminEmail
    );
    
    PRINT 'User tokens deleted successfully.';

    -- Step 5: Delete user sessions for non-admin users
    DELETE FROM UserSessions
    WHERE UserId IN (
        SELECT Id FROM AspNetUsers 
        WHERE Email != @AdminEmail
    );
    
    PRINT 'User sessions deleted successfully.';

    -- Step 6: Delete login history for non-admin users (if table exists)
    IF OBJECT_ID('LoginHistory', 'U') IS NOT NULL
    BEGIN
        DELETE FROM LoginHistory
        WHERE UserId IN (
            SELECT Id FROM AspNetUsers 
            WHERE Email != @AdminEmail
        );
        PRINT 'Login history deleted successfully.';
    END
    ELSE
    BEGIN
        PRINT 'LoginHistory table does not exist, skipping.';
    END

    -- Step 7: Count users to be deleted (excluding admin)
    SELECT @DeletedCount = COUNT(*) 
    FROM AspNetUsers 
    WHERE Email != @AdminEmail;

    -- Step 8: Delete non-admin users
    DELETE FROM AspNetUsers 
    WHERE Email != @AdminEmail;
    
    PRINT 'Users deleted: ' + CAST(@DeletedCount AS NVARCHAR(10));

    COMMIT TRANSACTION;
    
    -- Verify remaining users
    SELECT 
        Email, 
        UserName,
        FirstName,
        LastName,
        PersonId
    FROM AspNetUsers;
    
    PRINT '';
    PRINT 'Cleanup completed successfully! Only admin@hybridauth.local remains.';

END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    
    PRINT 'Error occurred during cleanup:';
    PRINT ERROR_MESSAGE();
    THROW;
END CATCH;
