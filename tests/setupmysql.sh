#!/bin/bash
echo 'Setting up mysql...'

until [[ -f /run/mysqld/mysqld.pid && $(du -b /run/mysqld/mysqld.pid | awk '{print $1}') -eq 2 ]]
do
  sleep 1
  echo 1
done

sleep 5

until [[ -f /run/mysqld/mysqlx.sock.lock && $(du -b /run/mysqld/mysqlx.sock.lock | awk '{print $1}') -eq 3 ]]
do
  sleep 1
  echo 3
done

echo 'mysql started.'

/usr/bin/mysql -pabcABC123 < /tests/testdataMysql.sql

echo 'Done!'
