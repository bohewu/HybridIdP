/*
Backfill role-less users to default 'User' role (SQL Server).

Use when existing accounts were created/provisioned without any role.
This script only affects users with zero role assignments.
*/

SET NOCOUNT ON;

DECLARE @UserRoleId UNIQUEIDENTIFIER;

SELECT TOP (1) @UserRoleId = [Id]
FROM [AspNetRoles]
WHERE [Name] = N'User';

IF @UserRoleId IS NULL
BEGIN
    RAISERROR(N"Role 'User' not found in AspNetRoles.", 16, 1);
    RETURN;
END;

;WITH RoleLessUsers AS
(
    SELECT u.[Id]
    FROM [AspNetUsers] u
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM [AspNetUserRoles] ur
        WHERE ur.[UserId] = u.[Id]
    )
)
INSERT INTO [AspNetUserRoles] ([UserId], [RoleId])
SELECT rlu.[Id], @UserRoleId
FROM RoleLessUsers rlu;

SELECT @@ROWCOUNT AS [RowsInserted];
