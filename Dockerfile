# --- Base image for build ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copy csproj and restore
COPY golfkollektivet-backend.csproj ./
RUN dotnet restore golfkollektivet-backend.csproj

# Copy everything else and publish
COPY . .
RUN dotnet publish -c Release -o /out /p:UseAppHost=false

# --- Runtime image ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Expose default Railway/Render/Fly port
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Copy published output
COPY --from=build /out ./

ENTRYPOINT ["dotnet", "golfkollektivet-backend.dll"]