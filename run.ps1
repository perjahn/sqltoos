#!/usr/bin/env pwsh
Set-StrictMode -v latest
$ErrorActionPreference = "Stop"

function Main() {
    [string] $password = Generate-AlphanumericPassword 20

    docker ps | grep -v "^CONTAINER" | awk '{print $1}' | xargs -r docker stop

    docker ps

    [string] $curdir = (Get-Location).Path
    [string] $bindmount = "$($curdir)/tests:/tests"

    [string] $containerImageMysql = "mysql"
    [string] $containerImagePostgres = "postgres"
    [string] $containerImageSqlserver = "mcr.microsoft.com/mssql/server"
    [string] $containerImageElasticsearch = "elasticsearch:8.14.3"

    [string] $containerMysql = $(docker ps | grep $containerImageMysql | awk '{print $1}')
    if ($containerMysql) {
        Log "Reusing existing mysql container: $containerMysql"
    }
    else {
        Log "Starting $($containerImageMysql):"
        docker run -d -p 3306:3306 -v $bindmount -e "MYSQL_ROOT_PASSWORD=$password" $containerImageMysql
        [string] $containerMysql = $(docker ps | grep $containerImageMysql | awk '{print $1}')
    }

    [string] $containerPostgres = $(docker ps | grep $containerImagePostgres | awk '{print $1}')
    if ($containerPostgres) {
        Log "Reusing existing postgres container: $containerPostgres"
    }
    else {
        Log "Starting $($containerImagePostgres):"
        docker run -d -p 5432:5432 -v $bindmount -e "POSTGRES_PASSWORD=$password" $containerImagePostgres
        [string] $containerPostgres = $(docker ps | grep $containerImagePostgres | awk '{print $1}')
    }

    [string] $containerSqlserver = $(docker ps | grep $containerImageSqlserver | awk '{print $1}')
    if ($containerSqlserver) {
        Log "Reusing existing sqlserver container: $containerSqlserver"
    }
    else {
        Log "Starting $($containerImageSqlserver):"
        docker run -d -p 1433:1433 -v $bindmount -e 'ACCEPT_EULA=Y' -e "SA_PASSWORD=$password" $containerImageSqlserver
        [string] $containerSqlserver = $(docker ps | grep $containerImageSqlserver | awk '{print $1}')
    }

    [string] $containerElasticsearch = $(docker ps | grep $containerImageElasticsearch | awk '{print $1}')
    if ($containerElasticsearch) {
        Log "Reusing existing elasticsearch container: $containerElasticsearch"
    }
    else {
        Log "Starting $($containerImageElasticsearch):"
        docker run -d -p 9200:9200 -v $bindmount -e 'discovery.type=single-node' -e "ELASTIC_PASSWORD=$password" $containerImageElasticsearch
        [string] $containerElasticsearch = $(docker ps | grep $containerImageElasticsearch | awk '{print $1}')
    }

    Download-SqlCmd
    Compile-Isatty

    Log "Waiting for containers to start up..."
    Start-Sleep 30

    Log "Running containers:"
    docker ps

    Log "Running mysql script in $($containerMysql):"
    docker exec $containerMysql /tests/setupmysql.sh

    Log "Running postgres script in $($containerPostgres):"
    docker exec $containerPostgres /tests/setuppostgres.sh

    Log "Running sqlserver script in $($containerSqlserver):"
    docker exec $containerSqlserver /tests/setupsqlserver.sh

    Log "Waiting for elasticsearch startup..."
    [int] $seconds = 0
    do {
        docker cp "$($containerElasticsearch):/usr/share/elasticsearch/config/certs/http_ca.crt" .
        Log ($seconds++)
        if ($seconds -eq 300 ) {
            Log "Couldn't retrieve elasticsearch ca cert."
            exit 1
        }
        Start-Sleep 1
    }
    while (!([IO.File]::Exists("http_ca.crt")) -or (dir http_ca.crt).Length -eq 0)
    Log ("Got elasticsearch cert: $((dir http_ca.crt).Length) bytes file.")

    [bool] $testfail = $false

    $env:SQLTOELASTIC_PASSWORD = $password

    Log "Importing mysql:"
    $env:SQLTOELASTIC_CONNSTR = "Server=localhost;Database=testdb;User Id=root;Password=$password"
    dotnet run --project src configMysql.json
    jq 'del(.took)' result.json > result_mysql.json
    diff result_mysql.json tests/expected_mysql.json
    if (!$?) {
        Log "Error: mysql."
        $testfail = $true
    }

    Log "Importing postgres:"
    $env:SQLTOELASTIC_CONNSTR = "Server=localhost;Database=testdb;User Id=postgres;Password=$password"
    dotnet run --project src configPostgres.json
    jq 'del(.took)' result.json > result_postgres.json
    diff result_postgres.json tests/expected_postgres.json
    if (!$?) {
        Log "Error: postgres."
        $testfail = $true
    }

    Log "Importing sqlserver:"
    $env:SQLTOELASTIC_CONNSTR = "Server=localhost;Database=testdb;User Id=sa;Password=$password"
    dotnet run --project src configSqlserver.json
    jq 'del(.took)' result.json > result_sqlserver.json
    diff result_sqlserver.json tests/expected_sqlserver.json
    if (!$?) {
        Log "Error: sqlserver."
        $testfail = $true
    }

    if ($testfail) {
        exit 1
    }
}

function Download-SqlCmd() {
    [string] $arch = $(uname -m | sed 's/^x86_64$/amd64/g')
    $releasesurl = 'https://api.github.com/repos/microsoft/go-sqlcmd/releases/latest'
    $regex = '^sqlcmd-linux-' + $arch + '\\.tar\\.bz2$'
    $jqpattern = '.assets[] | select(.name|test("' + $regex + '")) | .browser_download_url'
    $asseturl = $(curl -s "$releasesurl" | jq "$jqpattern" -r)
    if (!$asseturl) {
        Log "Couldn't find $regex in $releasesurl"
        exit 1
    }
    $filename = $(basename "$asseturl")
    Log "Downloading: '$asseturl' -> '$filename'"
    curl -Ls "$asseturl" -o "$filename"
    tar xf "$filename"
    rm "$filename"
    rm NOTICE.md
    rm sqlcmd_debug
    mv sqlcmd tests
}

function Compile-Isatty() {
    Log "Compiling isatty work around for mysql"
    echo "int isatty(int fd) { return 1; }" | gcc -O2 -fpic -shared -ldl -o tests/isatty.so -xc -
}

function Generate-AlphanumericPassword([int] $numberOfChars) {
    [char[]] $validChars = 'a'..'z' + 'A'..'Z' + [char]'0'..[char]'9'
    [string] $password = ""
    do {
        [string] $password = (1..$numberOfChars | ForEach-Object { $validChars[(Get-Random -Maximum $validChars.Length)] }) -join ""
    }
    while (
        !($password | ? { ($_.ToCharArray() | ? { [Char]::IsUpper($_) }) }) -or
        !($password | ? { ($_.ToCharArray() | ? { [Char]::IsLower($_) }) }) -or
        !($password | ? { ($_.ToCharArray() | ? { [Char]::IsDigit($_) }) }));

    return $password
}

function Log($message) {
    Write-Host $message -f Cyan
}

Main
