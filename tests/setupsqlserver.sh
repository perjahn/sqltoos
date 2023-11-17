#!/bin/bash
echo 'Setting up sqlserver...'

if [[ -f '/opt/mssql-tools/bin/sqlcmd' ]]; then
  echo "Using sqlcmd in '/opt/mssql-tools/bin'"
  PATH=/opt/mssql-tools/bin:$PATH
elif [[ -f '/tests/sqlcmd' ]]; then
  echo 'Using /tests/sqlcmd binary.'
  PATH=/tests:$PATH
else
  echo 'Assuming sqlcmd is in the path.'
fi

sqlcmd -U sa -P abcABC123 -i /tests/testdataSqlserver1.sql
sqlcmd -U sa -P abcABC123 -i /tests/testdataSqlserver2.sql

echo 'Done!'
