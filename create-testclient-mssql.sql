-- Create TestClient OAuth Application for E2E Testing (SQL Server)
-- This client is used by the TestClient application (https://localhost:7001)

SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM [OpenIddictApplications] WHERE [ClientId] = 'testclient-public')
BEGIN
    INSERT INTO [OpenIddictApplications] (
        [Id],
        [ClientId],
        [ClientType],
        [DisplayName],
        [ConsentType],
        [ConcurrencyToken],
        [RedirectUris],
        [PostLogoutRedirectUris],
        [Permissions]
    ) VALUES (
        NEWID(),
        'testclient-public',
        'public',
        'Test Client (Public)',
        'explicit',
        CAST(NEWID() AS nvarchar(36)),
        '["https://localhost:7001/signin-oidc"]',
        '["https://localhost:7001/signout-callback-oidc"]',
        '["ept:authorization","ept:token","ept:logout","gt:authorization_code","gt:refresh_token","scp:openid","scp:profile","scp:email","scp:roles","scp:api:company:read","scp:api:inventory:read"]'
    )
END

-- Verify
SELECT [ClientId], [DisplayName], [ClientType], [RedirectUris], [Permissions] 
FROM [OpenIddictApplications] 
WHERE [ClientId] = 'testclient-public';
