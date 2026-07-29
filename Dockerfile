# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
# Left unset by default so an unversioned `docker build` falls through to
# BookWheel.csproj's own InformationalVersion default instead of a second,
# hardcoded value that can drift from it.
ARG APP_VERSION=

COPY BookWheel.slnx ./
COPY BookWheel/BookWheel.csproj BookWheel/
COPY BookWheel.Tests/BookWheel.Tests.csproj BookWheel.Tests/
RUN dotnet restore BookWheel/BookWheel.csproj

COPY . .
RUN dotnet publish BookWheel/BookWheel.csproj -c Release -o /app/publish /p:UseAppHost=false ${APP_VERSION:+/p:InformationalVersion=$APP_VERSION}

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Listen on HTTP only inside the container; TLS should terminate upstream.
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

COPY --from=build /app/publish .

# Ensure runtime writable paths are owned by the non-root app user.
RUN mkdir -p /app/App_Data/logs /home/app/.aspnet/DataProtection-Keys \
	&& chown -R app:app /app /home/app/.aspnet

# Persist Data Protection keys for stable auth/credential protection across restarts.
VOLUME ["/home/app/.aspnet/DataProtection-Keys", "/app/App_Data"]

USER $APP_UID
ENTRYPOINT ["dotnet", "BookWheel.dll"]
