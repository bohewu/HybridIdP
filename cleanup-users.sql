-- Delete all users except admin (SQL Server syntax)
SET QUOTED_IDENTIFIER ON;
GO

DELETE FROM [AspNetUserRoles] 
WHERE [UserId] IN (
    SELECT [Id] FROM [AspNetUsers] 
    WHERE [Email] != 'admin@hybridauth.local'
);

DELETE FROM [AspNetUsers] 
WHERE [Email] != 'admin@hybridauth.local';

-- Remove persons not linked to admin's account
DELETE FROM [Persons]
WHERE [Id] NOT IN (
    SELECT [PersonId] FROM [AspNetUsers]
    WHERE [Email] = 'admin@hybridauth.local' AND [PersonId] IS NOT NULL
);

-- Verify remaining data
SELECT 'Remaining users' AS q, COUNT(*) as count FROM [AspNetUsers];
SELECT 'Remaining persons' AS q, COUNT(*) as count FROM [Persons];
