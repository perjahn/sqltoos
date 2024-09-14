#!/bin/bash
set -e
echo 'Setting up mysql...'

sleep 5

pushd /run/mysqld

until [[ \
  -e mysqld.pid && $(du -b mysqld.pid | awk '{print $1}') -eq 2 && \
  -e mysqld.sock && $(du -b mysqld.sock | awk '{print $1}') -eq 0 && \
  -e mysqld.sock.lock && $(du -b mysqld.sock.lock | awk '{print $1}') -eq 2 && \
  -e mysqlx.sock && $(du -b mysqlx.sock | awk '{print $1}') -eq 0 && \
  -e mysqlx.sock.lock && $(du -b mysqlx.sock.lock | awk '{print $1}') -eq 2 ]]
do
  ls -la
  sleep 1
  echo 1
done

popd

sleep 5

echo 'mysql started.'

LD_PRELOAD=/tests/isatty.so /usr/bin/mysql -p$MYSQL_ROOT_PASSWORD < /tests/testdataMysql.sql

echo 'Done!'
