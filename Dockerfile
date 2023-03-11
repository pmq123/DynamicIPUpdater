FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build-env
WORKDIR /DynamicIPUpdater

# Copy everything
COPY . ./
# Restore as distinct layers
RUN dotnet restore
# Build and publish a release
RUN dotnet publish -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:6.0
WORKDIR /DynamicIPUpdater
COPY --from=build-env /DynamicIPUpdater/out .
ENTRYPOINT ["dotnet", "DynamicIPUpdater.dll"]