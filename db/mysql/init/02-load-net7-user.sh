#!/usr/bin/env bash
# Load the account DB (net7_user) from the verbatim 2010 dump.
set -euo pipefail

DUMP=/dumps/net7_user.sql

if [[ ! -f "$DUMP" ]]; then
  echo "init: $DUMP not present, skipping" >&2
  exit 0
fi

echo "init: loading net7_user schema/data from $DUMP"
mysql --user=root --password="${MYSQL_ROOT_PASSWORD}" \
      --default-character-set=latin1 \
      net7_user < "$DUMP"
echo "init: net7_user loaded"
