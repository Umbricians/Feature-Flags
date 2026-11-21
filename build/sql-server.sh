#!/usr/bin/env bash
# Starts SQL Server in Docker for the integration tests, or reuses the container that is already there, and writes the
# connection string the tests read to tests/sqlserver.local.env. The file is git-ignored and holds the server's
# password, so this script never prints it. Run it from anywhere: build/sql-server.sh
set -euo pipefail

container=featureflags-sql
port=14330
env_file="$(cd "$(dirname "$0")/.." && pwd)/tests/sqlserver.local.env"

if ! command -v docker >/dev/null 2>&1; then
  echo "Docker is needed to run SQL Server for the integration tests." >&2
  exit 1
fi

if [ -f "$env_file" ]; then
  # Read the password back out of the file, so the container and the file always agree.
  set -a
  . "$env_file"
  set +a
else
  if docker container inspect "$container" >/dev/null 2>&1; then
    echo "The $container container exists but $env_file does not, so its password is not known." >&2
    echo "Remove the container with 'docker rm -f $container' and run this again." >&2
    exit 1
  fi
  # A random password that meets SQL Server's rules: upper case, lower case, digit and symbol.
  FEATUREFLAGS_SQL_PASSWORD="Aa1!$(openssl rand -hex 16)"
  umask 077
  {
    echo "FEATUREFLAGS_SQL_PASSWORD=\"$FEATUREFLAGS_SQL_PASSWORD\""
    echo "FEATUREFLAGS_SQLSERVER=\"Server=localhost,$port;User Id=sa;Password=$FEATUREFLAGS_SQL_PASSWORD;TrustServerCertificate=True;Encrypt=False\""
  } > "$env_file"
fi

if docker container inspect "$container" >/dev/null 2>&1; then
  docker start "$container" >/dev/null
else
  docker run --detach --name "$container" \
    --publish "$port:1433" \
    --env ACCEPT_EULA=Y \
    --env "MSSQL_SA_PASSWORD=$FEATUREFLAGS_SQL_PASSWORD" \
    mcr.microsoft.com/mssql/server:2022-latest >/dev/null
fi

echo "Waiting for SQL Server to accept connections..."
for _ in $(seq 1 60); do
  if docker exec "$container" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$FEATUREFLAGS_SQL_PASSWORD" -Q "select 1" >/dev/null 2>&1; then
    echo "SQL Server is ready on localhost:$port. The tests read tests/sqlserver.local.env."
    exit 0
  fi
  sleep 2
done

echo "SQL Server did not become ready in two minutes. See 'docker logs $container'." >&2
exit 1
