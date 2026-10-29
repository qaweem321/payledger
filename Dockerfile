# Build: full SDK image. Run: chiseled ASP.NET image (no shell, no package manager, non-root user).
# "-extra" adds ICU: Microsoft.Data.SqlClient refuses to run in invariant globalization mode.
FROM mcr.microsoft.com/dotnet/sdk:10.0.402-noble AS build
WORKDIR /src

# Restore first, from project files only, so code edits reuse the cached package layer.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/PayLedger.Domain/PayLedger.Domain.csproj src/PayLedger.Domain/
COPY src/PayLedger.Application/PayLedger.Application.csproj src/PayLedger.Application/
COPY src/PayLedger.Infrastructure/PayLedger.Infrastructure.csproj src/PayLedger.Infrastructure/
COPY src/PayLedger.Api/PayLedger.Api.csproj src/PayLedger.Api/
RUN dotnet restore src/PayLedger.Api/PayLedger.Api.csproj

COPY src/ src/
RUN dotnet publish src/PayLedger.Api/PayLedger.Api.csproj -c Release --no-restore -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "PayLedger.Api.dll"]
