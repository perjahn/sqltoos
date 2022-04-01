sqltoelastic - cli tool for exporting data from relation databases and importing into elasticsearch.


Github actions: [![Build](https://github.com/perjahn/sqltoelastic/workflows/Build/badge.svg)](https://github.com/perjahn/sqltoelastic/actions?query=workflow%3A%22Build%22)

## Usage

``Usage: sqltoelastic <configfile>``

Supports MySQL, PostgreSQL, and SQL Server.
Will probably not work with older elasticsearch version which requires doctype.
Using HttpClient and the _bulk endpoint in elasticsearch, i.e. not any bloated nest client.
Patches are welcome! :)

## Config file

The config file should be a json file, format below.
Optional fields may be set to empty string/empty array.
Example files: ```configMysql.json```, ```configPostgres.json``` and ```configSqlserver.json```.

```json
{
  "dbprovider": "",         # Name of database provider. Valid values: mysql/postgres/sqlserver
  "connstr": "",            # Database connection string.
  "sql": "",                # Select query.
  "serverurl": "",          # Url to elasticsearch. Example: http://localhost:9200
  "username": "",           # Elasticsearch username.
  "password": "",           # Elasticsearch password.
  "indexname": "",          # Prefix of elasticsearch index, will be composed to: indexname-{yyyy.MM}
  "timestampfield": "",     # Column that should be used as @timestamp (and index suffix).
  "idfield": "",            # Field that should be used for _id.
  "idprefix": "",           # Optional. Prefix text that should inserted into _id value.
  "toupperfields": [],      # Optional. Fields whose values should be upper cased.
  "tolowerfields": [],      # Optional. Fields whose values should be lower cased.
  "addconstantfields": [],  # Optional. Add contant field, using name=value syntax. Example: "zzz=999" and "extradate=2022-01-01T01:02:03"
  "expandjsonfields": [],   # Optional. Fields that contains json that should be expanded.
  "deescapefields": []      # Optional. Fields that contains backslash and/or quotes that should be de-escaped.
}
```

## Incremental scheduled run

A common use case is to only copy newly added/modified rows in a sql database, to elasticsearch.
Please setup [cron](https://en.wikipedia.org/wiki/Cron) to run sqltoelastic for this,
in combination with a sql query that includes a where condition using a date column relative to the current date,
perhaps with some overlap to prevent jitter timing problems.
Of course also make sure the id field is consistent to prevent duplicates in elasticsearch.
