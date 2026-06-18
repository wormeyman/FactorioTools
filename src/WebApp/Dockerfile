FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

COPY . .
# GenerateSwagger=false skips the PostBuild swagger regeneration: the swagger tool
# can't load the self-contained linux-musl-x64 output on this glibc build host, and
# the committed swagger.json is what the front-end client consumes anyway.
RUN dotnet publish src/WebApp/WebApp.csproj -c Release -o /app --self-contained --runtime linux-musl-x64 /p:PublishReadyToRun=true /p:GenerateSwagger=false

FROM mcr.microsoft.com/dotnet/runtime-deps:8.0-alpine
WORKDIR /app
COPY --from=build /app .

HEALTHCHECK CMD wget -qO- -t1 http://localhost:80/healthz || exit 1
ENTRYPOINT ["./Knapcode.FactorioTools.WebApp"]
