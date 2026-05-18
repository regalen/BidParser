# Stage 1: build frontend
FROM node:20-alpine AS frontend-build
WORKDIR /build
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ .
RUN npm run build

# Stage 2: publish .NET API
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-build
WORKDIR /src

COPY BidParser.sln Directory.Build.props Directory.Packages.props ./
COPY src/BidParser.Api/BidParser.Api.csproj src/BidParser.Api/
COPY src/BidParser.Domain/BidParser.Domain.csproj src/BidParser.Domain/
COPY src/BidParser.Infrastructure/BidParser.Infrastructure.csproj src/BidParser.Infrastructure/
COPY src/BidParser.Parsing/BidParser.Parsing.csproj src/BidParser.Parsing/
RUN dotnet restore src/BidParser.Api/BidParser.Api.csproj

COPY src/ src/
RUN dotnet publish src/BidParser.Api/BidParser.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# Stage 3: runtime with built frontend
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=dotnet-build /app/publish ./
COPY --from=frontend-build /build/dist ./wwwroot

VOLUME /data

ENV DATABASE_URL=sqlite:////data/db.sqlite
ENV UPLOAD_DIR=/data/files
ENV PORT=3447
ENV ASPNETCORE_URLS=http://0.0.0.0:3447
EXPOSE 3447

ENTRYPOINT ["dotnet", "BidParser.Api.dll"]
