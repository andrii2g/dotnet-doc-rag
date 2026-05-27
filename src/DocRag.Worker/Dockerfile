FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore DotnetDocRag.slnx --configfile NuGet.Config
RUN dotnet publish src/DocRag.Worker/DocRag.Worker.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /app/storage/documents /app/storage/tmp /app/import
ENTRYPOINT ["dotnet", "DocRag.Worker.dll"]
