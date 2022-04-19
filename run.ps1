#!/usr/bin/pwsh
Set-StrictMode -v latest
$ErrorActionPreference = "Stop"

function Main() {
    docker ps | grep -v "^CONTAINER" | awk '{print $1}' | xargs -r docker stop

    docker ps

    [string] $curdir = (Get-Location).Path
    [string] $bindmount = "$($curdir)/tests:/tests"

    [string] $containerMysql = $(docker ps | grep 'mysql' | awk '{print $1}')
    if ($containerMysql) {
        Log "Reusing existing mysql container: $containerMysql"
    }
    else {
        Log "Starting mysql:"
        docker run -d -p 3306:3306 -v $bindmount -e 'MYSQL_ROOT_PASSWORD=abcABC123' mysql
        [string] $containerMysql = $(docker ps | grep 'mysql' | awk '{print $1}')
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

    [string] $containerSqlserver = $(docker ps | grep 'mssql/server' | awk '{print $1}')
    if ($containerSqlserver) {
        Log "Reusing existing sqlserver container: $containerSqlserver"
    }
    else {
        Log "Starting sqlserver:"
        docker run -d -p 1433:1433 -v $bindmount -e 'ACCEPT_EULA=Y' -e 'SA_PASSWORD=abcABC123' 'mcr.microsoft.com/mssql/server'
        [string] $containerSqlserver = $(docker ps | grep 'mssql/server' | awk '{print $1}')
    }

    [string] $containerElasticsearch = $(docker ps | grep 'elasticsearch' | awk '{print $1}')
    if ($containerElasticsearch) {
        Log "Reusing existing elasticsearch container: $containerElasticsearch"
    }
    else {
        Log "Starting elasticsearch:"
        docker run -d -p 9200:9200 -e discovery.type=single-node -e bootstrap.memory_lock=true -e 'ES_JAVA_OPTS=-Xms1024m -Xmx1024m' elasticsearch:7.17.1
        [string] $containerElasticsearch = $(docker ps | grep 'mcr.microsoft.com/mssql/server' | awk '{print $1}')
    }

    Log "Running containers:"
    docker ps


    [string[]] $filenames = "testdataMysql.sql", "testdataPostgres2.sql", "testdataSqlserver2.sql"

    foreach ($filename in $filenames) {
        $sb = New-Object Text.StringBuilder

        [string] $header = Get-Content (Join-Path "tests" $filename) -Raw
        $sb.AppendLine($header) | Out-Null

        for ([int] $i = 0; $i -lt 10; $i++) {
            [string] $datarow = "insert into testtable (somestring, somejsonstring, someint, somedate) values (null, 'abc$($i)', $($i + 1000), '2022-02-01 01:02:03');"
#            $sb.AppendLine($datarow) | Out-Null
        }

        [string] $outfile = Join-Path "tests" ([IO.Path]::ChangeExtension($filename, ".temp.sql"))
        Log "Using temporary sql script: '$outfile'"
        Set-Content $outfile $sb.ToString() -NoNewline
    }


    Log "Running mysql scripts:"
    docker exec $containerMysql "/tests/setupmysql.sh"

    Log "Running postgres scripts:"
    docker exec $containerPostgres "/usr/bin/psql" -U "postgres" -f "/tests/testdataPostgres1.sql"
    docker exec $containerPostgres "/usr/bin/psql" -U "postgres" -d "testdb" -f "/tests/testdataPostgres2.temp.sql"

    Log "Running sqlserver scripts:"
    docker exec $containerSqlserver "/opt/mssql-tools/bin/sqlcmd" -U "sa" -P "abcABC123" -i "/tests/testdataSqlserver1.sql"
    docker exec $containerSqlserver "/opt/mssql-tools/bin/sqlcmd" -U "sa" -P "abcABC123" -i "/tests/testdataSqlserver2.temp.sql"

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
