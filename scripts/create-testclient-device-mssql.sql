-- Create TestClient Device OAuth Application (SQL Server)

SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM [OpenIddictApplications] WHERE [ClientId] = 'testclient-device')
BEGIN
    INSERT INTO [OpenIddictApplications] (
        [Id],
        [ClientId],
        [ClientType],
        [DisplayName],
        [ConsentType],
        [ConcurrencyToken],
        [Permissions]
    ) VALUES (
        NEWID(),
        'testclient-device',
        'public',
        'Test Client (Device)',
        'explicit',
        CAST(NEWID() AS nvarchar(36)),
        '["ept:device","ept:token","gt:device_code","gt:refresh_token","scp:openid","scp:profile","scp:email"]'
    )
END
GO

SELECT [ClientId], [Permissions] FROM [OpenIddictApplications] WHERE [ClientId] = 'testclient-device';
GO
