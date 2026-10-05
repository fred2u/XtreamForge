FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# the project files are restored first, so that the restored packages are cached while only the sources change;
# central package management needs Directory.Packages.props
COPY ["Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/XtreamForge.Web/XtreamForge.Web.csproj", "src/XtreamForge.Web/"]
COPY ["src/XtreamForge.ServiceDefaults/XtreamForge.ServiceDefaults.csproj", "src/XtreamForge.ServiceDefaults/"]
RUN dotnet restore "src/XtreamForge.Web/XtreamForge.Web.csproj"

COPY . .
# restored again now that the sources are present: publishing on the project-only restore above left the framework web assets
# (_framework/blazor.web.js) out of the image
RUN dotnet publish "src/XtreamForge.Web/XtreamForge.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "XtreamForge.Web.dll"]
