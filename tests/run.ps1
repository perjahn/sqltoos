#!/usr/bin/env pwsh
Set-StrictMode -v latest
$ErrorActionPreference = "Stop"

function Main() {
    [string] $password = Generate-AlphanumericPassword 20

    docker ps | grep -v "^CONTAINER" | awk '{print $1}' | xargs -r docker stop

    docker ps

    [string] $containerImageMysql = "mysql"
    [string] $containerImagePostgres = "postgres"
    [string] $containerImageSqlserver = "mcr.microsoft.com/mssql/server"
    [string] $containerImageOpensearch = "opensearchproject/opensearch"

    [string] $curdir = (Get-Location).Path

    [string] $bindmountMysql = "$($curdir)/mysql:/tests"
    [string] $bindmountPostgres = "$($curdir)/postgres:/tests"
    [string] $bindmountSqlserver = "$($curdir)/sqlserver:/tests"

    [string] $containerMysql = $(docker ps | grep $containerImageMysql | awk '{print $1}')
    if ($containerMysql) {
        Log "Reusing existing mysql container: $containerMysql"
    }
    else {
        Log "Starting $($containerImageMysql):"
        docker run -d -p 3306:3306 -v $bindmountMysql -e "MYSQL_ROOT_PASSWORD=$password" $containerImageMysql
        [string] $containerMysql = $(docker ps | grep $containerImageMysql | awk '{print $1}')
    }

    [string] $containerPostgres = $(docker ps | grep $containerImagePostgres | awk '{print $1}')
    if ($containerPostgres) {
        Log "Reusing existing postgres container: $containerPostgres"
    }
    else {
        Log "Starting $($containerImagePostgres):"
        docker run -d -p 5432:5432 -v $bindmountPostgres -e "POSTGRES_PASSWORD=$password" $containerImagePostgres
        [string] $containerPostgres = $(docker ps | grep $containerImagePostgres | awk '{print $1}')
    }

    [string] $containerSqlserver = $(docker ps | grep $containerImageSqlserver | awk '{print $1}')
    if ($containerSqlserver) {
        Log "Reusing existing sqlserver container: $containerSqlserver"
    }
    else {
        Log "Starting $($containerImageSqlserver):"
        docker run -d -p 1433:1433 -v $bindmountSqlserver -e 'ACCEPT_EULA=Y' -e "SA_PASSWORD=$password" $containerImageSqlserver
        [string] $containerSqlserver = $(docker ps | grep $containerImageSqlserver | awk '{print $1}')
    }

    [string] $containerOpensearch = $(docker ps | grep $containerImageOpensearch | awk '{print $1}')
    if ($containerOpensearch) {
        Log "Reusing existing opensearch container: $containerOpensearch"
    }
    else {
        Log "Starting $($containerImageOpensearch):"
        docker run -d -p 9200:9200 -e 'discovery.type=single-node' -e "OPENSEARCH_INITIAL_ADMIN_PASSWORD=$password" $containerImageOpensearch
        [string] $containerOpensearch = $(docker ps | grep $containerImageOpensearch | awk '{print $1}')
    }

    Download-SqlCmd "sqlserver"
    Compile-Isatty "mysql"

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

    Log "Waiting for opensearch startup..."
    [int] $seconds = 0
    do {
        [string] $certfilename = "esnode.pem"
        if ([IO.File]::Exists($certfilename)) {
            del $certfilename
        }
        docker cp "$($containerOpensearch):/usr/share/opensearch/config/$certfilename" .
        Log ($seconds++)
        if ($seconds -eq 300 ) {
            Log "Couldn't retrieve opensearch ca cert."
            exit 1
        }
        Start-Sleep 1
    }
    while (!([IO.File]::Exists($certfilename)) -or (dir $certfilename).Length -eq 0)
    Log ("Got opensearch cert: $((dir $certfilename).Length) bytes file.")
    $env:SQLTOOS_CACERTFILE = $certfilename

    [bool] $success = $true

    $env:SQLTOOS_PASSWORD = $password
    dotnet --version

    Log "Importing mysql:"
    [string] $resultfilename = "result.json"
    if ([IO.File]::Exists($resultfilename)) {
        del $resultfilename
    }
    $env:SQLTOOS_CONNSTR = "Server=localhost;Database=testdb;User Id=root;Password=$password"
    dotnet run --project ../src configMysql.json
    if (!$?) {
        Log "Error: mysql run."
        $success = $false
    }
    else {
        jq 'del(.took)' result.json > result_mysql.json
        diff result_mysql.json expected_mysql.json
        if (!$?) {
            Log "Error: mysql diff."
            $success = $false
        }
    }

    Log "Importing postgres:"
    [string] $resultfilename = "result.json"
    if ([IO.File]::Exists($resultfilename)) {
        del $resultfilename
    }
    $env:SQLTOOS_CONNSTR = "Server=localhost;Database=testdb;User Id=postgres;Password=$password"
    dotnet run --project ../src configPostgres.json
    if (!$?) {
        Log "Error: postgres run."
        $success = $false
    }
    else {
        jq 'del(.took)' result.json > result_postgres.json
        diff result_postgres.json expected_postgres.json
        if (!$?) {
            Log "Error: postgres diff."
            $success = $false
        }
    }

    Log "Importing sqlserver:"
    [string] $resultfilename = "result.json"
    if ([IO.File]::Exists($resultfilename)) {
        del $resultfilename
    }
    $env:SQLTOOS_CONNSTR = "Server=localhost;TrustServerCertificate=true;Database=testdb;User Id=sa;Password=$password"
    dotnet run --project ../src configSqlserver.json
    if (!$?) {
        Log "Error: sqlserver run."
        $success = $false
    }
    else {
        jq 'del(.took)' result.json > result_sqlserver.json
        diff result_sqlserver.json expected_sqlserver.json
        if (!$?) {
            Log "Error: sqlserver diff."
            $success = $false
        }
    }

    if (!$success) {
        exit 1
    }
}

function Download-SqlCmd([string] $outputpath) {
    [string] $arch = $(uname -m | sed 's/^x86_64$/amd64/g')
    [string] $releasesurl = 'https://api.github.com/repos/microsoft/go-sqlcmd/releases/latest'
    [string] $regex = '^sqlcmd-linux-' + $arch + '\\.tar\\.bz2$'
    [string] $jqpattern = '.assets[] | select(.name|test("' + $regex + '")) | .browser_download_url'

    [string] $result = curl -sS $releasesurl
    if (!$?) {
        Write-Host $result
        Write-Host "Retrying download: '$releasesurl'" -f Yellow
        [string] $result = curl -sS $releasesurl
        if (!$?) {
            Write-Host $result
            Write-Host "Couldn't download sqlcmd (api): '$releasesurl'" -f Red
            exit 1
        }
    }

    [string] $asseturl = $($result | jq $jqpattern -r)
    if (!$asseturl) {
        Log "Couldn't find '$regex' in '$releasesurl'"
        exit 1
    }
    [string] $filename = $(basename $asseturl)
    Log "Downloading: '$asseturl' -> '$filename'"

    curl -LsS $asseturl -o $filename
    if (!$?) {
        Write-Host "Retrying download: '$asseturl'" -f Yellow
        curl -LsS $asseturl -o $filename
        if (!$?) {
            Write-Host "Couldn't download sqlcmd (asset): '$asseturl'" -f Red
            exit 1
        }
    }

    Write-Host "Extracting: '$filename'"
    tar xf $filename

    if ((dir sqlcmd).Length -eq 0) {
        Write-Host "Couldn't download sqlcmd (zero filelength)." -f Red
        exit 1
    }

    rm $filename
    rm NOTICE.md
    rm sqlcmd_debug
    mv sqlcmd $outputpath
}

function Compile-Isatty([string] $outputpath) {
    [string] $curdir = (Get-Location).Path
    [string] $dockerfile = 'FROM ubuntu
WORKDIR /out
RUN apt-get update && \
    apt-get -y install gcc
RUN echo "Compiling isatty work around for mysql" && \
    echo "int isatty(int fd) { return 1; }" | gcc -O2 -fpic -shared -ldl -o /out/isatty.so -xc -'

    Set-Content Dockerfile $dockerfile
    $env:DOCKER_BUILDKIT = 0
    docker build -t mysqlworkaround .
    rm Dockerfile

    rm -rf artifacts
    mkdir artifacts
    docker run --entrypoint cp -v $curdir/artifacts:/artifacts mysqlworkaround /out/isatty.so /artifacts
    docker rmi -f mysqlworkaround
    mv artifacts/isatty.so $outputpath
    rmdir artifacts
}

function Generate-AlphanumericPassword([int] $numberOfChars) {
    [char[]] $validChars = 'a'..'z' + 'A'..'Z' + [char]'0'..[char]'9'
    [string] $password = ""
    do {
        [string] $password = (1..$numberOfChars | ForEach-Object { $validChars[(Get-Random -Maximum $validChars.Length)] }) -join ""
    }
    while (
        !($password | Where-Object { ($_.ToCharArray() | Where-Object { [Char]::IsUpper($_) }) }) -or
        !($password | Where-Object { ($_.ToCharArray() | Where-Object { [Char]::IsLower($_) }) }) -or
        !($password | Where-Object { ($_.ToCharArray() | Where-Object { [Char]::IsDigit($_) }) }));

    return $password
}

function Log($message) {
    Write-Host $message -f Cyan
}

Main
