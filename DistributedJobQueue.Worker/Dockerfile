# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files and restore dependencies
COPY BrokerService/DistributedJobQueue.Core.csproj BrokerService/
COPY DistributedJobQueue.Worker/DistributedJobQueue.Worker.csproj DistributedJobQueue.Worker/
RUN dotnet restore DistributedJobQueue.Worker/DistributedJobQueue.Worker.csproj

# Copy all source and build
COPY . .
RUN dotnet publish DistributedJobQueue.Worker/DistributedJobQueue.Worker.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DistributedJobQueue.Worker.dll"]