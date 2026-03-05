/*
Backfill role-less users to default 'User' role (PostgreSQL).

Use when existing accounts were created/provisioned without any role.
This script only affects users with zero role assignments.
*/

DO $$
DECLARE
    user_role_id uuid;
    inserted_count integer;
BEGIN
    SELECT "Id"
    INTO user_role_id
    FROM "AspNetRoles"
    WHERE "Name" = 'User'
    LIMIT 1;

    IF user_role_id IS NULL THEN
        RAISE EXCEPTION 'Role ''User'' not found in AspNetRoles.';
    END IF;

    INSERT INTO "AspNetUserRoles" ("UserId", "RoleId")
    SELECT u."Id", user_role_id
    FROM "AspNetUsers" u
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM "AspNetUserRoles" ur
        WHERE ur."UserId" = u."Id"
    );

    GET DIAGNOSTICS inserted_count = ROW_COUNT;
    RAISE NOTICE 'RowsInserted=%', inserted_count;
END $$;
