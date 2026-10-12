#!/usr/bin/env bash
# Restores a BookWheel database dump (gzipped SQL `*.sql.gz` as written by the
# docker-compose `backup` sidecar, or a plain `.sql` file) into a running Postgres.
#
# Usage:
#   scripts/restore.sh <dump-file> [--host H] [--port P] [--db D] [--user U] [--force]
#
# The password is read from PGPASSWORD, falling back to POSTGRES_PASSWORD.
# Defaults: host=localhost port=5432 db=bookwheel user=bookwheel.
#
# Safety: if the target database already contains user tables, the script refuses
# to run unless --force is given. Stop the app before restoring.
set -euo pipefail

usage() {
  cat >&2 <<USAGE
Usage: $0 <dump-file> [--host H] [--port P] [--db D] [--user U] [--force]
  Password is read from PGPASSWORD or POSTGRES_PASSWORD.
USAGE
  exit 2
}

[[ $# -ge 1 ]] || usage

DUMP=""
HOST="localhost"
PORT="5432"
DB="bookwheel"
USER_NAME="bookwheel"
FORCE=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --host)  [[ $# -ge 2 ]] || usage; HOST="$2"; shift 2 ;;
    --port)  [[ $# -ge 2 ]] || usage; PORT="$2"; shift 2 ;;
    --db)    [[ $# -ge 2 ]] || usage; DB="$2"; shift 2 ;;
    --user)  [[ $# -ge 2 ]] || usage; USER_NAME="$2"; shift 2 ;;
    --force) FORCE=1; shift ;;
    -h|--help) usage ;;
    -*) echo "Unknown option: $1" >&2; usage ;;
    *)
      [[ -z "$DUMP" ]] || usage
      DUMP="$1"; shift ;;
  esac
done

[[ -n "$DUMP" ]] || usage

if [[ ! -f "$DUMP" ]]; then
  echo "Dump file not found: $DUMP" >&2
  exit 1
fi

export PGPASSWORD="${PGPASSWORD:-${POSTGRES_PASSWORD:-}}"
PSQL=(psql -h "$HOST" -p "$PORT" -U "$USER_NAME" -d "$DB" -v ON_ERROR_STOP=1)

TABLE_COUNT=$("${PSQL[@]}" -qtAX -c \
  "SELECT count(*) FROM pg_tables WHERE schemaname NOT IN ('pg_catalog', 'information_schema');")

if [[ "$TABLE_COUNT" -gt 0 && "$FORCE" -ne 1 ]]; then
  echo "WARNING: database '$DB' on $HOST:$PORT already contains $TABLE_COUNT table(s)." >&2
  echo "Restoring over existing data can overwrite or duplicate it. Re-run with --force to proceed." >&2
  exit 1
fi

echo "Restoring '$DUMP' into '$DB' on $HOST:$PORT as $USER_NAME ..."
if [[ "$DUMP" == *.gz ]]; then
  gunzip -c "$DUMP" | "${PSQL[@]}" -q > /dev/null
else
  "${PSQL[@]}" -q -f "$DUMP" > /dev/null
fi
echo "Restore complete."
