# SENECARE web app (ASP.NET Core MVC). Used by Render (render.yaml) and any Docker host.
# The CSS is compiled before committing (wwwroot/css/app.css), so the image needs no Node.js.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# global.json is left out on purpose: it pins the exact SDK of the developers' machines.
# .editorconfig carries the analyzer settings (e.g. CA1716 off): without it the build rules differ.
COPY .editorconfig Directory.Build.props Directory.Packages.props SeneCare.slnx ./
COPY src/ src/
# Warnings stay errors on the developers' machines (same SDK as global.json, tests). Here the SDK of the image
# may be newer: a new analyzer rule or NuGet advisory must not block a deploy of code already checked.
RUN dotnet publish src/SeneCare.Web/SeneCare.Web.csproj -c Release -o /app     -p:SkipTailwind=true -p:TreatWarningsAsErrors=false -p:EnforceCodeStyleInBuild=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
# Fonts for the PDF invoices and reports (QuestPDF).
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*
ENV TZ=Africa/Dakar \
    ASPNETCORE_ENVIRONMENT=Production
COPY --from=build /app .
# Uploaded photos (App_Data/uploads): writable by the non-root user of the image.
RUN mkdir -p /app/App_Data && chown -R $APP_UID /app/App_Data
USER $APP_UID
EXPOSE 8080
# Render (and most hosts) give the port in $PORT.
ENTRYPOINT ["sh", "-c", "ASPNETCORE_HTTP_PORTS=${PORT:-8080} exec dotnet SeneCare.Web.dll"]
