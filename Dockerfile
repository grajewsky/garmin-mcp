FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props GarminMcp.sln ./
COPY src/GarminMcp/GarminMcp.csproj src/GarminMcp/
RUN dotnet restore src/GarminMcp/GarminMcp.csproj
COPY src/GarminMcp/ src/GarminMcp/
RUN dotnet publish src/GarminMcp/GarminMcp.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "GarminMcp.dll"]
