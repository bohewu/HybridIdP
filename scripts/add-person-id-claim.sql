-- Add person_id to ClaimDefinitions
IF NOT EXISTS (SELECT 1 FROM ClaimDefinitions WHERE Name = 'person_id')
BEGIN
    INSERT INTO ClaimDefinitions (Id, Name, DisplayName, Description, ClaimType, UserPropertyPath, DataType, IsStandard, IsRequired)
    VALUES (
        NEWID(),
        'person_id',
        'Person ID',
        'Unique identifier linking user to Person entity',
        'person_id',
        'PersonId',
        'String',
        0, -- IsStandard = false
        0  -- IsRequired = false
    );
    PRINT 'Added person_id to ClaimDefinitions';
END
ELSE
BEGIN
    PRINT 'person_id already exists in ClaimDefinitions';
END

-- Add person_id to profile scope mapping
DECLARE @PersonIdClaimId UNIQUEIDENTIFIER;
DECLARE @ProfileScopeId NVARCHAR(450);

SELECT @PersonIdClaimId = Id FROM ClaimDefinitions WHERE Name = 'person_id';
SELECT @ProfileScopeId = Id FROM OpenIddictScopes WHERE Name = 'profile';

IF @PersonIdClaimId IS NOT NULL AND @ProfileScopeId IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM ScopeClaims WHERE ClaimDefinitionId = @PersonIdClaimId AND ScopeId = @ProfileScopeId)
    BEGIN
        INSERT INTO ScopeClaims (Id, ScopeId, ScopeName, ClaimDefinitionId, AlwaysInclude)
        VALUES (
            NEWID(),
            @ProfileScopeId,
            'profile',
            @PersonIdClaimId,
            0 -- AlwaysInclude = false
        );
        PRINT 'Added person_id to profile scope mapping';
    END
    ELSE
    BEGIN
        PRINT 'person_id is already mapped to profile scope';
    END
END
ELSE
BEGIN
    PRINT 'ERROR: Could not find person_id claim or profile scope';
END

-- Verify the changes
SELECT 
    c.Name AS ClaimName,
    s.ScopeName
FROM ScopeClaims s
JOIN ClaimDefinitions c ON s.ClaimDefinitionId = c.Id
WHERE s.ScopeName = 'profile'
ORDER BY c.Name;
