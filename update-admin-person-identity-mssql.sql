-- Phase 10.6.2: Data Migration - Set Admin Person Identity
-- Purpose: Update existing admin Person records with default NationalId
-- Database: SQL Server
-- Date: 2025-12-01

SET QUOTED_IDENTIFIER ON;
GO

-- Update admin Person records with default National ID
-- Assumes admin user email is 'admin@hybridauth.local'
UPDATE p
SET 
    p.NationalId = 'A123456789',
    p.IdentityDocumentType = 'NationalId',
    p.IdentityVerifiedAt = GETUTCDATE(),
    p.IdentityVerifiedBy = u.Id,
    p.ModifiedAt = GETUTCDATE(),
    p.ModifiedBy = u.Id
FROM Persons p
INNER JOIN AspNetUsers u ON p.Id = u.PersonId
WHERE u.Email = 'admin@hybridauth.local'
  AND p.NationalId IS NULL;

GO

-- Verify the update
SELECT 
    p.Id AS PersonId,
    p.FirstName,
    p.LastName,
    p.NationalId,
    p.IdentityDocumentType,
    p.IdentityVerifiedAt,
    u.Email AS AdminEmail
FROM Persons p
INNER JOIN AspNetUsers u ON p.Id = u.PersonId
WHERE u.Email = 'admin@hybridauth.local';

GO
