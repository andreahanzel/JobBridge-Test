# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["JobBridge.csproj", "./"]
RUN dotnet restore "JobBridge.csproj"
COPY . .
RUN dotnet build "JobBridge.csproj" -c Release -o /app/build
RUN dotnet publish "JobBridge.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Create data directory for SQLite
RUN mkdir -p /app/data

# Set environment variables
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 10000
ENTRYPOINT ["dotnet", "JobBridge.dll"]