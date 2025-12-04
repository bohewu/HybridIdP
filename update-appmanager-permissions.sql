-- Update ApplicationManager role permissions
UPDATE AspNetRoles 
SET Permissions = 'clients.read,clients.create,clients.update,clients.delete,scopes.read,scopes.create,scopes.update,scopes.delete',
    Description = 'Application Manager - can manage OAuth clients and scopes they own'
WHERE NormalizedName = 'APPLICATIONMANAGER';

-- Verify the update
SELECT Id, Name, NormalizedName, Permissions, Description FROM AspNetRoles WHERE NormalizedName = 'APPLICATIONMANAGER';
