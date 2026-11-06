# The OpenIGI dedicated server, for Linux hosts.
#
#   tools/release/package.sh server linux-x64 <version>      (or let CI build it)
#   docker build -f tools/server/Dockerfile --build-arg SERVER=artifacts/stage/server-linux-x64/openigi-server-<version>-linux-x64 -t openigi-server .
#
# The game's data is never in the image: mount your own installation at /game. Settings, bans and logs live in /data.
# See docker-compose.yml beside this file.

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0

ARG SERVER
COPY ${SERVER} /opt/openigi-server/
RUN useradd --system --home-dir /data --shell /usr/sbin/nologin openigi \
    && mkdir -p /data /game \
    && cp /opt/openigi-server/server.cfg.example /data/server.cfg.example \
    && chown -R openigi:openigi /data \
    && chmod 755 /opt/openigi-server/OpenIGI.Server

USER openigi
WORKDIR /data
VOLUME ["/data", "/game"]
EXPOSE 26001/udp

# Without a server.cfg in /data the example's defaults are used; copy it to server.cfg and edit to change them.
ENTRYPOINT ["/opt/openigi-server/OpenIGI.Server", "-config", "/data/server.cfg", "-game", "/game"]
