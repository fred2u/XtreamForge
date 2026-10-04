FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# the project files are restored first, so that the restored packages are cached while only the sources change;
# central package management needs Directory.Packages.props
COPY ["Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/XtreamForge.ApiService/XtreamForge.ApiService.csproj", "src/XtreamForge.ApiService/"]
COPY ["src/XtreamForge.Database/XtreamForge.Database.csproj", "src/XtreamForge.Database/"]
COPY ["src/XtreamForge.Domain/XtreamForge.Domain.csproj", "src/XtreamForge.Domain/"]
COPY ["src/XtreamForge.ServiceDefaults/XtreamForge.ServiceDefaults.csproj", "src/XtreamForge.ServiceDefaults/"]
RUN dotnet restore "src/XtreamForge.ApiService/XtreamForge.ApiService.csproj"

COPY . .
RUN dotnet publish "src/XtreamForge.ApiService/XtreamForge.ApiService.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "XtreamForge.ApiService.dll"]
