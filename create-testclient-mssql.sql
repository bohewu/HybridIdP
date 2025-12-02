-- Create TestClient OAuth Application for E2E Testing (SQL Server)
-- This client is used by the TestClient application (https://localhost:7001)

SET QUOTED_IDENTIFIER ON;
GO

-- Step 1: Create required API scopes if they don't exist
INSERT INTO [OpenIddictScopes] ([Id], [Name], [DisplayName], [Description], [ConcurrencyToken])
SELECT NEWID(), 'api:company:read', 'Read Company Data', 'Allows reading company information', CAST(NEWID() AS nvarchar(36))
WHERE NOT EXISTS (SELECT 1 FROM [OpenIddictScopes] WHERE [Name] = 'api:company:read');

INSERT INTO [OpenIddictScopes] ([Id], [Name], [DisplayName], [Description], [ConcurrencyToken])
SELECT NEWID(), 'api:company:write', 'Write Company Data', 'Allows creating and updating company information', CAST(NEWID() AS nvarchar(36))
WHERE NOT EXISTS (SELECT 1 FROM [OpenIddictScopes] WHERE [Name] = 'api:company:write');

INSERT INTO [OpenIddictScopes] ([Id], [Name], [DisplayName], [Description], [ConcurrencyToken])
SELECT NEWID(), 'api:inventory:read', 'Read Inventory Data', 'Allows reading inventory information', CAST(NEWID() AS nvarchar(36))
WHERE NOT EXISTS (SELECT 1 FROM [OpenIddictScopes] WHERE [Name] = 'api:inventory:read');
GO

-- Step 2: Create TestClient application
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
        '["ept:authorization","ept:token","ept:logout","gt:authorization_code","gt:refresh_token","response_type:code","scp:openid","scp:profile","scp:email","scp:roles","scp:api:company:read","scp:api:company:write","scp:api:inventory:read"]'
    )
END
GO

-- Step 3: Verify scopes
SELECT [Name], [DisplayName] FROM [OpenIddictScopes] 
WHERE [Name] IN ('openid', 'profile', 'email', 'roles', 'api:company:read', 'api:company:write', 'api:inventory:read')
ORDER BY [Name];
GO

-- Step 4: Verify client
SELECT [ClientId], [DisplayName], [ClientType], [RedirectUris], [Permissions] 
FROM [OpenIddictApplications] 
WHERE [ClientId] = 'testclient-public';
GO
