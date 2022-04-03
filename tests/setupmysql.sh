#!/bin/bash
echo 'Waiting for sock files...'

until [[ -f /run/mysqld/mysqld.pid && $(du -b /run/mysqld/mysqld.pid | awk '{print $1}') -eq 2 ]]
do
  sleep 1
  echo .
done

until [[ -f /run/mysqld/mysqld.sock.lock && $(du -b /run/mysqld/mysqld.sock.lock | awk '{print $1}') -eq 2 ]]
do
  sleep 1
  echo .
done

until [[ -f /run/mysqld/mysqlx.sock.lock && $(du -b /run/mysqld/mysqlx.sock.lock | awk '{print $1}') -eq 3 ]]
do
  sleep 1
  echo .
done

echo 'Done!'

/usr/bin/mysql -pabcABC123 < /tests/testdataMysql.temp.sql
