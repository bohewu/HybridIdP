-- Delete all persons except the admin person (SQL Server syntax)
-- This script preserves the person linked to admin user
-- Finds admin's PersonId via AspNetUsers table

SET QUOTED_IDENTIFIER ON;
GO

-- Delete all persons except the one linked to admin user
DELETE FROM [Persons] 
WHERE [Id] NOT IN (
    SELECT [PersonId] FROM [AspNetUsers] 
    WHERE [Email] = 'admin@hybridauth.local' AND [PersonId] IS NOT NULL
);
