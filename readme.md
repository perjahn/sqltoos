[![Build](https://github.com/perjahn/sqltoos/actions/workflows/build.yml/badge.svg)](https://github.com/perjahn/sqltoos/actions/workflows/build.yml)
[![CodeQL](https://github.com/perjahn/sqltoos/actions/workflows/github-code-scanning/codeql/badge.svg)](https://github.com/perjahn/sqltoos/actions/workflows/github-code-scanning/codeql)

sqltoos - cli tool for exporting data from relational databases and importing into opensearch.

## Usage

``Usage: sqltoos <configfile>``

Supports MySQL, PostgreSQL, and SQL Server.
Will probably not work with older opensearch versions which require doctype.
Using HttpClient and the _bulk endpoint in opensearch, i.e. not any bloated nest client.
Patches are welcome! :)

## Config file

The config file should be a json file, format below.
Optional fields may be set to empty string/empty array.
Example files in tests folder: ```configMysql.json```, ```configPostgres.json``` and ```configSqlserver.json```.

```json
{
  "dbprovider": "",         # Name of database provider. Valid values: mysql/postgres/sqlserver
  "connstr": "",            # Database connection string.
  "sql": "",                # Select query.
  "opensearchserverurl": "",  # Url to opensearch. Example: http://localhost:9200
  "cacertfile": "",         # Optional. Assume https cert is ok, if signed using this ca cert file.
  "allowinvalidhttpscert": false,  # Optional, default false. Don't validate https cert.
  "username": "",           # Opensearch username.
  "password": "",           # Opensearch password.
  "indexname": "",          # Prefix of opensearch index, will be composed to: indexname-{yyyy.MM}
  "timestampfield": "",     # Column that should be used as @timestamp (and index suffix).
  "idfield": "",            # Field that should be used for _id.
  "idprefix": "",           # Optional. Prefix text that should be inserted into _id value.
  "toupperfields": [],      # Optional. Fields whose values should be upper cased.
  "tolowerfields": [],      # Optional. Fields whose values should be lower cased.
  "addconstantfields": [],  # Optional. Add contant field, using name=value syntax. Example: "zzz=999" and "extradate=2022-01-01T01:02:03"
  "expandjsonfields": [],   # Optional. Fields that contains json that should be expanded.
  "deescapefields": []      # Optional. Fields that contains backslash and/or quotes that should be de-escaped.
}
```

## Environment variables

All config values in the config file can be overridden by using environment variables instead, which has
precedence over values from the config file. To specify a value using an environment variable, precede the
name of the value with "SQLTOOS_", and then add the upper cased name of the value.
E.g. SQLTOOS_PASSWORD for replacing the password value.
Multivalue values (arrays) should be comma separated.

## Incremental scheduled run

A common use case is to only copy newly added/modified rows in a sql database, to opensearch.
Please setup [cron](https://en.wikipedia.org/wiki/Cron) to run sqltoos for this,
in combination with a sql query that includes a where condition using a date column relative to the current date,
perhaps with some overlap to prevent jitter timing problems.
Of course also make sure the id field is consistent to prevent duplicates in opensearch.
