-- Delete all users except admin
DELETE FROM "AspNetUserRoles" 
WHERE "UserId" IN (
    SELECT "Id" FROM "AspNetUsers" 
    WHERE "Email" != 'admin@hybridauth.local'
);

DELETE FROM "AspNetUsers" 
WHERE "Email" != 'admin@hybridauth.local';
