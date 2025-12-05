-- Create M2M TestClient for Client Credentials Flow (SQL Server)
-- This client is used for machine-to-machine authentication testing

SET QUOTED_IDENTIFIER ON;
GO

-- Step 1: Ensure API scopes exist (not public scopes)
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

-- Step 2: Ensure scope extensions exist with IsPublic=0 for API scopes
INSERT INTO [ScopeExtensions] ([ScopeId], [IsPublic], [IsRequired], [DisplayOrder])
SELECT s.[Id], 0, 0, 0
FROM [OpenIddictScopes] s
WHERE s.[Name] IN ('api:company:read', 'api:company:write', 'api:inventory:read')
AND NOT EXISTS (
    SELECT 1 FROM [ScopeExtensions] se WHERE se.[ScopeId] = s.[Id]
);
GO

-- Step 3: Create M2M TestClient application (confidential client with secret)
-- Client Secret: "m2m-test-secret-2024" (will be hashed by OpenIddict automatically)
IF NOT EXISTS (SELECT 1 FROM [OpenIddictApplications] WHERE [ClientId] = 'testclient-m2m')
BEGIN
    INSERT INTO [OpenIddictApplications] (
        [Id],
        [ClientId],
        [ClientSecret],
        [ClientType],
        [DisplayName],
        [ConsentType],
        [ConcurrencyToken],
        [Permissions]
    ) VALUES (
        NEWID(),
        'testclient-m2m',
        'm2m-test-secret-2024',
        'confidential',
        'Test M2M Client (Confidential)',
        'implicit',
        CAST(NEWID() AS nvarchar(36)),
        '["ept:token","ept:introspection","ept:revocation","gt:client_credentials","scp:api:company:read","scp:api:company:write","scp:api:inventory:read"]'
    )
END
GO

-- Step 4: Verify scopes
SELECT [Name], [DisplayName] FROM [OpenIddictScopes] 
WHERE [Name] IN ('api:company:read', 'api:company:write', 'api:inventory:read')
ORDER BY [Name];
GO

-- Step 5: Verify client
SELECT [ClientId], [DisplayName], [ClientType], [Permissions] 
FROM [OpenIddictApplications] 
WHERE [ClientId] = 'testclient-m2m';
GO

-- Step 6: Verify scope extensions (API scopes should have IsPublic=0)
SELECT s.[Name], se.[IsPublic]
FROM [OpenIddictScopes] s
INNER JOIN [ScopeExtensions] se ON se.[ScopeId] = s.[Id]
WHERE s.[Name] IN ('api:company:read', 'api:company:write', 'api:inventory:read');
GO

