# Stage 1: build frontend
FROM node:22-alpine AS frontend-build
ARG APP_VERSION=dev
ENV VITE_APP_VERSION=$APP_VERSION
WORKDIR /build
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ .
RUN npm run build

# Stage 2: publish .NET API
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-build
# The SemVer stamped into the API assembly. CI passes the tag; the default marks an untagged build.
ARG BIDPARSER_VERSION=1.0.0-dev
WORKDIR /src

# Every project in the API's reference graph must be here, or the restore layer silently skips one
# and the publish below fails on its missing assets file. The desktop project is deliberately
# absent: it is not part of the web image.
COPY BidParser.sln Directory.Build.props Directory.Packages.props ./
COPY src/BidParser.Api/BidParser.Api.csproj src/BidParser.Api/
COPY src/BidParser.Application/BidParser.Application.csproj src/BidParser.Application/
COPY src/BidParser.Domain/BidParser.Domain.csproj src/BidParser.Domain/
COPY src/BidParser.Infrastructure/BidParser.Infrastructure.csproj src/BidParser.Infrastructure/
COPY src/BidParser.Output/BidParser.Output.csproj src/BidParser.Output/
COPY src/BidParser.Parsing/BidParser.Parsing.csproj src/BidParser.Parsing/
RUN dotnet restore src/BidParser.Api/BidParser.Api.csproj

COPY src/ src/
RUN dotnet publish src/BidParser.Api/BidParser.Api.csproj \
    --configuration Release \
    --no-restore \
    -p:BidParserVersion=$BIDPARSER_VERSION \
    --output /app/publish

# Stage 3: runtime with built frontend
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=dotnet-build /app/publish ./
COPY --from=frontend-build /build/dist ./wwwroot

VOLUME /data

ENV UPLOAD_DIR=/data/files
ENV PORT=3447
ENV ASPNETCORE_URLS=http://0.0.0.0:3447
EXPOSE 3447

ENTRYPOINT ["dotnet", "BidParser.Api.dll"]
