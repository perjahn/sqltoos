#!/usr/bin/pwsh
Set-StrictMode -v latest
$ErrorActionPreference = "Stop"

function Main() {
    docker ps | awk '{print $1}' | xargs docker stop

    docker ps

    [string] $curdir = (Get-Location).Path
    [string] $bindmount = "$($curdir)/tests:/tests"

    [string] $containerSqlserver = $(docker ps | grep 'mssql/server' | awk '{print $1}')
    if ($containerSqlserver) {
        Log "Reusing existing sqlserver container: $containerSqlserver"
    }
    else {
        Log "Starting sqlserver:"
        docker run -d -p 1433:1433 -v $bindmount -e 'ACCEPT_EULA=Y' -e 'SA_PASSWORD=abcABC123' 'mcr.microsoft.com/mssql/server'
        [string] $containerSqlserver = $(docker ps | grep 'mssql/server' | awk '{print $1}')
    }

    [string] $containerPostgres = $(docker ps | grep 'postgres' | awk '{print $1}')
    if ($containerPostgres) {
        Log "Reusing existing postgres container: $containerPostgres"
    }
    else {
        Log "Starting postgres:"
        docker run -d -p 5432:5432 -v $bindmount -e 'POSTGRES_PASSWORD=abcABC123' postgres
        [string] $containerPostgres = $(docker ps | grep 'postgres' | awk '{print $1}')
    }

    [string] $containerMysql = $(docker ps | grep 'mysql' | awk '{print $1}')
    if ($containerMysql) {
        Log "Reusing existing mysql container: $containerMysql"
    }
    else {
        Log "Starting mysql:"
        docker run -d -p 3306:3306 -v $bindmount -e 'MYSQL_ROOT_PASSWORD=abcABC123' mysql
        [string] $containerMysql = $(docker ps | grep 'mysql' | awk '{print $1}')
    }

    [string] $containerElasticsearch = $(docker ps | grep 'elasticsearch' | awk '{print $1}')
    if ($containerElasticsearch) {
        Log "Reusing existing elasticsearch container: $containerElasticsearch"
    }
    else {
        Log "Starting elasticsearch:"
        docker run -d -p 9200:9200 -e discovery.type=single-node -e bootstrap.memory_lock=true -e 'ES_JAVA_OPTS=-Xms1024m -Xmx1024m' elasticsearch:7.16.2
        [string] $containerElasticsearch = $(docker ps | grep 'mcr.microsoft.com/mssql/server' | awk '{print $1}')
    }

    Log "Running containers:"
    docker ps

    Log "Running sqlserver scripts:"
    docker exec $containerSqlserver /opt/mssql-tools/bin/sqlcmd -U sa -P abcABC123 -i /tests/testdataSqlserver1.sql
    docker exec $containerSqlserver /opt/mssql-tools/bin/sqlcmd -U sa -P abcABC123 -i /tests/testdataSqlserver2.sql

    Log "Running postgres scripts:"
    docker exec $containerPostgres /usr/bin/psql -U postgres -f /tests/testdataPostgres1.sql
    docker exec $containerPostgres /usr/bin/psql -U postgres -d testdb -f /tests/testdataPostgres2.sql

    Log "Running mysql scripts:"
    docker exec $containerMysql /tests/setupmysql.sh

    Log "Waiting for elasticsearch startup..."
    sleep 45

    Log "Importing sqlserver:"
    dotnet run configSqlserver.json
    Log "Importing postgres:"
    dotnet run configPostgres.json
    Log "Importing mysql:"
    dotnet run configMysql.json
}

function Log($message) {
    Write-Host $message -f Cyan
}

Main
