# Phase AP -- rolling Postgres -> private S3 backup sidecar.
#
# Based on the same postgres major version as the server's database
# (docker-compose postgres:16) so `pg_dump` is never older than the server it
# dumps. Adds the AWS CLI for S3 upload/list/delete and bash for the loop.
FROM postgres:16-alpine

# aws-cli: S3 put/list/delete via a bucket-scoped token.
# bash + coreutils: the backup loop (the script needs bash arrays + date -u).
RUN apk add --no-cache aws-cli bash coreutils

COPY backup.sh /usr/local/bin/backup.sh
RUN chmod +x /usr/local/bin/backup.sh

# Runs as the postgres image's default user; it only needs network access to
# Postgres + S3 and /tmp for the in-flight dump file. No volumes, no ports.
ENTRYPOINT ["/usr/local/bin/backup.sh"]
