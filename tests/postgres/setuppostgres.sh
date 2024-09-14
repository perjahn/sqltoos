#!/bin/bash
set -e
echo 'Setting up postgres...'

/usr/bin/psql -U postgres -f /tests/testdataPostgres1.sql
/usr/bin/psql -U postgres -d testdb -f /tests/testdataPostgres2.sql

echo 'Done!'
