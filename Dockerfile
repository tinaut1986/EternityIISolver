# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj files and restore dependencies
COPY ["EternityServer/EternityServer.csproj", "EternityServer/"]
COPY ["EternityShared/EternityShared.csproj", "EternityShared/"]
RUN dotnet restore "EternityServer/EternityServer.csproj"

# Copy the rest of the code
COPY . .
WORKDIR "/src/EternityServer"
RUN dotnet build "EternityServer.csproj" -c Release -o /app/build

# Stage 2: Publish
FROM build AS publish
RUN dotnet publish "EternityServer.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 3: Final runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Copy published files
COPY --from=publish /app/publish .

# Copy CSV files for seeding (Program.cs looks for them in ../)
# We put the server in /app/server and CSVs in /app
WORKDIR /app/server
COPY --from=build /src/Data/eternity2_256.csv /app/Data/
COPY --from=build /src/Data/eternity2_256_all_hints.csv /app/Data/
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "EternityServer.dll"]
