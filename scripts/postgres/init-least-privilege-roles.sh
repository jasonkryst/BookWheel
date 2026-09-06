#!/bin/bash
# Creates two non-superuser roles for the app to use instead of the Postgres
# bootstrap role (POSTGRES_USER): bookwheel_migrator (schema owner, used only
# for the one-time startup migration) and bookwheel_app (SELECT/INSERT/UPDATE/
# DELETE only, used for all runtime request handling). The bootstrap role
# itself can never have its SUPERUSER attribute revoked (Postgres enforces
# this — "the bootstrap user must have the SUPERUSER attribute") so instead
# the app simply never connects as that role again after this script runs.
#
# Runs automatically against a FRESH data volume (mounted into
# /docker-entrypoint-initdb.d/), where no tables exist yet — bookwheel_migrator
# will own every table it creates during the app's first startup migration, so
# no ownership transfer is needed here.
#
# For an EXISTING deployment's volume (tables already exist, owned by the
# bootstrap role), run this script manually AND transfer table ownership
# explicitly afterward — see the "Migrating an existing deployment" section in
# README.md's Data Storage documentation for the exact commands (REASSIGN
# OWNED cannot be used here: Postgres refuses it because the bootstrap role
# also owns the database itself, which REASSIGN OWNED cannot touch).
set -euo pipefail

: "${POSTGRES_DB:?POSTGRES_DB must be set}"
: "${POSTGRES_USER:?POSTGRES_USER must be set}"
: "${POSTGRES_MIGRATOR_PASSWORD:?POSTGRES_MIGRATOR_PASSWORD must be set}"
: "${POSTGRES_APP_PASSWORD:?POSTGRES_APP_PASSWORD must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
    DO \$\$
    BEGIN
        IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'bookwheel_migrator') THEN
            CREATE ROLE bookwheel_migrator WITH LOGIN PASSWORD '$POSTGRES_MIGRATOR_PASSWORD';
        ELSE
            ALTER ROLE bookwheel_migrator WITH LOGIN PASSWORD '$POSTGRES_MIGRATOR_PASSWORD';
        END IF;

        IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'bookwheel_app') THEN
            CREATE ROLE bookwheel_app WITH LOGIN PASSWORD '$POSTGRES_APP_PASSWORD';
        ELSE
            ALTER ROLE bookwheel_app WITH LOGIN PASSWORD '$POSTGRES_APP_PASSWORD';
        END IF;
    END
    \$\$;

    -- CREATE on the database itself (not just the schema) is required for
    -- CREATE EXTENSION citext, which InitialCreate provisions.
    GRANT CONNECT, CREATE ON DATABASE $POSTGRES_DB TO bookwheel_migrator;
    GRANT CREATE, USAGE ON SCHEMA public TO bookwheel_migrator;

    GRANT CONNECT ON DATABASE $POSTGRES_DB TO bookwheel_app;
    GRANT USAGE ON SCHEMA public TO bookwheel_app;
    GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO bookwheel_app;
    GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO bookwheel_app;
    ALTER DEFAULT PRIVILEGES FOR ROLE bookwheel_migrator IN SCHEMA public
        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO bookwheel_app;
    ALTER DEFAULT PRIVILEGES FOR ROLE bookwheel_migrator IN SCHEMA public
        GRANT USAGE, SELECT ON SEQUENCES TO bookwheel_app;
EOSQL

echo "bookwheel_migrator and bookwheel_app roles provisioned; the bootstrap $POSTGRES_USER role is no longer used by the application."
