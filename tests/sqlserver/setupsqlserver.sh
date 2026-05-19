#!/bin/bash
set -e
echo 'Setting up sqlserver...'

export SQLCMD_TELEMETRY='false'

if [ -f '/tests/sqlcmd' ]; then
  echo 'Using /tests/sqlcmd binary.'
  PATH=/tests:$PATH
else
  echo 'sqlcmd not found.'
fi

sqlcmd -U sa -P $SA_PASSWORD -i /tests/testdataSqlserver1.sql
sqlcmd -U sa -P $SA_PASSWORD -i /tests/testdataSqlserver2.sql

echo "Hostname: '$(hostname)'"

echo 'Done!'
