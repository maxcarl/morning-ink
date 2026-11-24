# Build natively on the runner's platform and cross-compile for the target architecture.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
# "1.2.0" for a release, "1.2.0-3-gabc1234" for a build from main after it; shown on the account page.
ARG VERSION=dev
WORKDIR /src
COPY . .
RUN dotnet publish src/MorningInk.Web -c Release -a $TARGETARCH -o /app -p:InformationalVersion=$VERSION

FROM mcr.microsoft.com/dotnet/aspnet:10.0
LABEL org.opencontainers.image.licenses="AGPL-3.0-or-later" \
      org.opencontainers.image.source="https://github.com/maxcarl/morning-ink"
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir /data \
    && chown $APP_UID /data
USER $APP_UID
WORKDIR /app
COPY --from=build /app .
# The image ships third-party binaries: their licenses travel with it.
COPY LICENSE THIRD-PARTY-NOTICES.md ./
VOLUME /data
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "MorningInk.Web.dll"]
