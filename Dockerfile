FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8007
ENV ASPNETCORE_URLS=http://+:8007

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Notification.Api/Notification.Api.csproj", "Notification.Api/"]
COPY ["Notification.Application/Notification.Application.csproj", "Notification.Application/"]
COPY ["Notification.Domain/Notification.Domain.csproj", "Notification.Domain/"]
COPY ["Notification.Infrastructure/Notification.Infrastructure.csproj", "Notification.Infrastructure/"]
RUN dotnet restore "Notification.Api/Notification.Api.csproj"
COPY . .
RUN dotnet build "Notification.Api/Notification.Api.csproj" -c Release --no-restore -o /app/build

FROM build AS publish
RUN dotnet publish "Notification.Api/Notification.Api.csproj" -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Notification.Api.dll"]
