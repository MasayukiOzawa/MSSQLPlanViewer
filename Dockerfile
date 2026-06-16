FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["MSSQLPlanViewer.sln", "./"]
COPY ["src/MSSQLPlanViewer.Core/MSSQLPlanViewer.Core.csproj", "src/MSSQLPlanViewer.Core/"]
COPY ["src/MSSQLPlanViewer.Web/MSSQLPlanViewer.Web.csproj", "src/MSSQLPlanViewer.Web/"]
COPY ["tests/MSSQLPlanViewer.Core.Tests/MSSQLPlanViewer.Core.Tests.csproj", "tests/MSSQLPlanViewer.Core.Tests/"]
RUN dotnet restore "./MSSQLPlanViewer.sln"

COPY . .
RUN dotnet publish "./src/MSSQLPlanViewer.Web/MSSQLPlanViewer.Web.csproj" \
    -c Release \
    --no-restore \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:5293
EXPOSE 5293

COPY --from=build /app/publish .

USER app
ENTRYPOINT ["dotnet", "MSSQLPlanViewer.Web.dll"]
