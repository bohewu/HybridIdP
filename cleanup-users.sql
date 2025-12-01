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
