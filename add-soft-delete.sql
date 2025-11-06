ALTER TABLE "AspNetUsers" ADD COLUMN IF NOT EXISTS "IsDeleted" boolean DEFAULT false;
ALTER TABLE "AspNetUsers" ADD COLUMN IF NOT EXISTS "DeletedAt" timestamp without time zone;
ALTER TABLE "AspNetUsers" ADD COLUMN IF NOT EXISTS "DeletedBy" uuid;
