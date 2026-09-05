# The same E0 image on a plain Ubuntu base instead of the devcontainers one.
#
# It exists because the measurement said to try it. Of the 1.7 GB the devcontainers base version
# unpacks to, 405 MB is that image's git feature, which builds git from source, and 195 MB is its
# common-utils feature. Neither is something a .NET lesson touches, and git from apt is a fraction
# of the size. This file is here so that "the base image is most of the download" is a claim with
# a second number next to it rather than an opinion.
#
# Everything below the base is deliberately identical to Dockerfile. If the two drift, the
# comparison stops meaning anything.

FROM ubuntu:24.04

ARG DOTNET_SDK_VERSION=10.0.400

RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      lldb \
      curl \
      ca-certificates \
      git \
      sudo \
 && rm -rf /var/lib/apt/lists/*

# The devcontainers base image supplies this user, so a plain base has to make one. Same name and
# same uid, because devcontainer.json names the user and a Codespace expects to be able to sudo.
# Ubuntu 24.04 ships its own account at uid 1000, so that one goes first or useradd refuses.
RUN userdel -r ubuntu 2>/dev/null || true \
 && useradd -m -s /bin/bash -u 1000 vscode \
 && echo 'vscode ALL=(root) NOPASSWD:ALL' > /etc/sudoers.d/vscode

USER vscode
ENV DOTNET_ROOT=/home/vscode/.dotnet
ENV PATH=/home/vscode/.dotnet:/home/vscode/.dotnet/tools:$PATH
ENV DOTNET_NOLOGO=1
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
 && bash /tmp/dotnet-install.sh --version "${DOTNET_SDK_VERSION}" --install-dir "$DOTNET_ROOT" \
 && rm /tmp/dotnet-install.sh

RUN dotnet tool install --global dotnet-counters \
 && dotnet tool install --global dotnet-trace \
 && dotnet tool install --global dotnet-dump \
 && dotnet tool install --global dotnet-gcdump

COPY --chown=vscode:vscode .devcontainer/install-checked-jit.sh /tmp/install-checked-jit.sh
RUN bash /tmp/install-checked-jit.sh && rm /tmp/install-checked-jit.sh

COPY --chown=vscode:vscode global.json Directory.Build.props ClrXray.slnx /tmp/warm/
COPY --chown=vscode:vscode tools /tmp/warm/tools
RUN cd /tmp/warm \
 && dotnet build ClrXray.slnx -c Release \
 && cd / \
 && rm -rf /tmp/warm
