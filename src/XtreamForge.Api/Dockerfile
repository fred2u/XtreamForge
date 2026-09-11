FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["global.json", "."]
COPY ["Directory.Build.props", "."]
COPY ["XtreamForge.slnx", "."]
COPY ["src/XtreamForge.Api/XtreamForge.Api.csproj", "src/XtreamForge.Api/"]
COPY ["src/XtreamForge.Infrastructure/XtreamForge.Infrastructure.csproj", "src/XtreamForge.Infrastructure/"]
COPY ["src/XtreamForge.ServiceDefaults/XtreamForge.ServiceDefaults.csproj", "src/XtreamForge.ServiceDefaults/"]
RUN dotnet restore "src/XtreamForge.Api/XtreamForge.Api.csproj"

COPY . .
RUN dotnet publish "src/XtreamForge.Api/XtreamForge.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "XtreamForge.Api.dll"]
