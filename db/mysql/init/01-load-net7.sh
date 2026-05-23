#!/usr/bin/env bash
# Load the content DB (net7) from the verbatim 2010 dump. The mysql
# image's entrypoint pre-creates the MYSQL_USER/PASSWORD; we run as root
# here because the dump touches metadata tables and grants.
set -euo pipefail

DUMP=/dumps/net7.sql

if [[ ! -f "$DUMP" ]]; then
  echo "init: $DUMP not present, skipping" >&2
  exit 0
fi

echo "init: loading net7 schema/data from $DUMP"
mysql --user=root --password="${MYSQL_ROOT_PASSWORD}" \
      --default-character-set=latin1 \
      net7 < "$DUMP"
echo "init: net7 loaded"
