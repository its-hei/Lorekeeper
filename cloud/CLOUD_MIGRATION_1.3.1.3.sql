-- Lorekeeper Cloud migration for client-version-aware canonical updates.
-- Run ONCE against the D1 database before deploying Worker v2.

ALTER TABLE translations ADD COLUMN client_version TEXT;

-- Existing canonical rows were created by the pre-1.3.1.3 client line.
-- Backfill them so older clients cannot later masquerade as a newer correction.
UPDATE translations
SET client_version = '1.3.1.1'
WHERE client_version IS NULL
   OR TRIM(client_version) = '';
