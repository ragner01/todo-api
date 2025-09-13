# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ConsoleApp1/*.csproj ConsoleApp1/
RUN dotnet restore ConsoleApp1/ConsoleApp1.csproj
COPY . .
RUN dotnet publish ConsoleApp1/ConsoleApp1.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
EXPOSE 8080
EXPOSE 8081
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ConsoleApp1.dll"]

