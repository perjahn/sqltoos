#!/bin/bash
set -e
echo 'Setting up sqlserver...'

export SQLCMD_TELEMETRY='false'

if [[ -f '/opt/mssql-tools/bin/sqlcmd' ]]; then
  echo "Using sqlcmd in '/opt/mssql-tools/bin'"
  PATH=/opt/mssql-tools/bin:$PATH
elif [[ -f '/tests/sqlcmd' ]]; then
  echo 'Using /tests/sqlcmd binary.'
  PATH=/tests:$PATH
else
  echo 'Assuming sqlcmd is in the path.'
  find / -name 'sqlcmd'
fi

sqlcmd -U sa -P $SA_PASSWORD -i /tests/testdataSqlserver1.sql
sqlcmd -U sa -P $SA_PASSWORD -i /tests/testdataSqlserver2.sql

echo 'Done!'
