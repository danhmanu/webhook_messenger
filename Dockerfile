FROM node:22-alpine AS frontend-build
WORKDIR /frontend

COPY package*.json ./
RUN npm ci

COPY index.html tsconfig.json tsconfig.app.json vite.config.ts ./
COPY src ./src
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY MessengerOpenAiWebhook.csproj ./
RUN dotnet restore

COPY . ./
COPY --from=frontend-build /frontend/wwwroot ./wwwroot
RUN dotnet publish MessengerOpenAiWebhook.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "MessengerOpenAiWebhook.dll"]
