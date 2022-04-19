#!/usr/bin/env pwsh
Set-StrictMode -v latest
$ErrorActionPreference = "Stop"

function Main() {
    docker ps | grep -v "^CONTAINER" | awk '{print $1}' | xargs -r docker stop

    docker ps

    [string] $curdir = (Get-Location).Path
    [string] $bindmount = "$($curdir)/tests:/tests"

    [string] $containerImage = "mysql"
    [string] $containerMysql = $(docker ps | grep $containerImage | awk '{print $1}')
    if ($containerMysql) {
        Log "Reusing existing mysql container: $containerMysql"
    }
    else {
        Log "Starting $($containerImage):"
        docker run -d -p 3306:3306 -v $bindmount -e 'MYSQL_ROOT_PASSWORD=abcABC123' $containerImage
        [string] $containerMysql = $(docker ps | grep $containerImage | awk '{print $1}')
    }

    [string] $containerImage = "postgres"
    [string] $containerPostgres = $(docker ps | grep $containerImage | awk '{print $1}')
    if ($containerPostgres) {
        Log "Reusing existing postgres container: $containerPostgres"
    }
    else {
        Log "Starting $($containerImage):"
        docker run -d -p 5432:5432 -v $bindmount -e 'POSTGRES_PASSWORD=abcABC123' $containerImage
        [string] $containerPostgres = $(docker ps | grep $containerImage | awk '{print $1}')
    }

    [string] $containerImage = "mcr.microsoft.com/azure-sql-edge"
    [string] $containerSqlserver = $(docker ps | grep $containerImage | awk '{print $1}')
    if ($containerSqlserver) {
        Log "Reusing existing sqlserver container: $containerSqlserver"
    }
    else {
        Log "Starting $($containerImage):"
        docker run -d -p 1433:1433 -v $bindmount -e 'ACCEPT_EULA=Y' -e 'SA_PASSWORD=abcABC123' $containerImage
        [string] $containerSqlserver = $(docker ps | grep $containerImage | awk '{print $1}')
    }

    [string] $containerImage = "elasticsearch:7.17.2"
    [string] $containerElasticsearch = $(docker ps | grep $containerImage | awk '{print $1}')
    if ($containerElasticsearch) {
        Log "Reusing existing elasticsearch container: $containerElasticsearch"
    }
    else {
        Log "Starting $($containerImage):"
        docker run -d -p 9200:9200 -e 'discovery.type=single-node' -e 'bootstrap.memory_lock=true' -e 'ES_JAVA_OPTS=-Xms1024m -Xmx1024m' $containerImage
        [string] $containerElasticsearch = $(docker ps | grep $containerImage | awk '{print $1}')
    }

    Log "Running containers:"
    docker ps

    Log "Running mysql script in $($containerMysql):"
    docker exec $containerMysql /tests/setupmysql.sh

    Log "Running postgres script in $($containerPostgres):"
    docker exec $containerPostgres /tests/setuppostgres.sh

    Log "Running sqlserver script in $($containerSqlserver):"
    docker exec $containerSqlserver /tests/setupsqlserver.sh

    Log "Waiting for elasticsearch startup..."
    sleep 15

    Log "Importing mysql:"
    dotnet run --project src configMysql.json
    jq 'del(.took)' result.json > result_mysql.json
    diff result_mysql.json tests/expected_mysql.json
    if (!$?) {
        Log "Error: mysql."
    }

    Log "Importing postgres:"
    dotnet run --project src configPostgres.json
    jq 'del(.took)' result.json > result_postgres.json
    diff result_postgres.json tests/expected_postgres.json
    if (!$?) {
        Log "Error: postgres."
    }

    Log "Importing sqlserver:"
    dotnet run --project src configSqlserver.json
    jq 'del(.took)' result.json > result_sqlserver.json
    diff result_sqlserver.json tests/expected_sqlserver.json
    if (!$?) {
        Log "Error: sqlserver."
    }
}

function Log($message) {
    Write-Host $message -f Cyan
}

Main
