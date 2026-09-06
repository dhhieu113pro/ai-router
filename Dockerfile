FROM node:24-alpine AS admin-build
WORKDIR /src/src/AiRouter.Admin

COPY src/AiRouter.Admin/package.json ./
RUN npm install --no-audit --no-fund

COPY src/AiRouter.Admin/ ./
RUN npm test
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
COPY --from=admin-build /src/src/AiRouter.Admin/dist/ai-router-admin/browser ./src/AiRouter.Server/wwwroot/admin

RUN dotnet restore src/AiRouter.Server/AiRouter.Server.csproj
RUN dotnet publish src/AiRouter.Server/AiRouter.Server.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    AIROUTER_DATA_PATH=/data/ai-router.db \
    AIROUTER_ADMIN_KEY= \
    AIROUTER_API_KEY=

COPY --from=build /app/publish .
RUN mkdir -p /data && chown -R ${APP_UID}:0 /data /app

USER ${APP_UID}
EXPOSE 8080

ENTRYPOINT ["dotnet", "AiRouter.Server.dll"]
