FROM mcr.microsoft.com/dotnet/nightly/sdk:11.0-preview as build

WORKDIR /app

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV POWERSHELL_TELEMETRY_OPTOUT=1
ENV SQLCMD_TELEMETRY='false'

COPY . .

RUN dotnet --version && \
    dotnet publish -c Release

FROM mcr.microsoft.com/dotnet/nightly/runtime:11.0-preview as runtime

WORKDIR /app

COPY --from=build /app/src/bin/Release/net11.0/* /app/

ENTRYPOINT ["/app/sqltoos"]
