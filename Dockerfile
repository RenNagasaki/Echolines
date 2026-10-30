# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# NuGet.config comes first and on its own: the author's machine has a private company feed configured
# globally that answers 401. The file clears the source list down to nuget.org, and restore fails
# without it.
COPY NuGet.config ./
COPY Echolines.sln ./
COPY Echolines.Server/Echolines.Server.csproj Echolines.Server/
COPY Echolines.Tests/Echolines.Tests.csproj Echolines.Tests/
RUN dotnet restore Echolines.Server/Echolines.Server.csproj

COPY . .

# ⚠ NOT `--no-restore`, and that is the whole point.
#
# The restore above runs when only the .csproj files exist — the usual layer-caching trick. At that
# moment the project has no .razor files in it, so the SDK does not consider it a Blazor project and
# never adds Microsoft.AspNetCore.App.Internal.Assets to the dependency graph. That package is where
# blazor.web.js actually lives; it is not part of the SDK. With `--no-restore` here, the graph is
# never revisited once the source arrives, and the publish quietly produces a wwwroot without
# _framework at all. The admin UI then renders perfectly and responds to nothing, because the script
# that starts the circuit answers 404.
#
# The restore above still earns its layer: it warms every other package, so this second pass is cheap.
# Reproduced cold in WSL both ways — with `--no-restore` the assets are missing, without it they are there.
RUN dotnet publish Echolines.Server/Echolines.Server.csproj -c Release -o /app

# Fail the build rather than ship an admin UI that renders and does nothing. Everything in it — the
# checkboxes, the release buttons, the filters — runs over the Blazor circuit, and the circuit cannot
# start without this file. Its absence is invisible from the outside: the pages still look right.
RUN test -s /app/wwwroot/_framework/blazor.web.js \
    || (echo "FATAL: blazor.web.js missing from the publish output — the admin UI would be inert. See the SDK pin above." >&2 && exit 1)

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# Non-root: the service takes anonymous writes from the open internet by design, so it should not be
# root in its own container as well.
USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Echolines.Server.dll"]
