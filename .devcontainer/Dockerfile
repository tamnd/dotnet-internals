# The E0 image. A reader clicks once and lands here, and every lesson in the book runs with no
# further setup, including the Part VI lessons that need a checked JIT.
#
# Built on plain Ubuntu with the SDK installed by version rather than on a prebuilt .NET image,
# because this project pins an exact SDK and a tagged image gives you whichever patch was current
# when the tag was pushed. The version below is the one thing here that moves with the pin.
#
# The build context is the repository root, not this directory, because the last step warms the
# NuGet cache against the repository's own project.

FROM mcr.microsoft.com/devcontainers/base:ubuntu-24.04

ARG DOTNET_SDK_VERSION=10.0.400

# lldb is the one large apt line and it is not optional: Part VII walks a reader through SOS
# against a live process. The rest is what dotnet-install.sh and the checked JIT walk need.
RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      lldb \
      curl \
      ca-certificates \
 && rm -rf /var/lib/apt/lists/*

USER vscode
ENV DOTNET_ROOT=/home/vscode/.dotnet
ENV PATH=/home/vscode/.dotnet:/home/vscode/.dotnet/tools:$PATH

# The first run of any dotnet command otherwise spends time on a welcome message and a telemetry
# notice, which would land in a reader's first lesson output.
ENV DOTNET_NOLOGO=1
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
 && bash /tmp/dotnet-install.sh --version "${DOTNET_SDK_VERSION}" --install-dir "$DOTNET_ROOT" \
 && rm /tmp/dotnet-install.sh

# The diagnostics tools a lesson invokes by name. Installed into the image rather than restored per
# lesson so that a reader's first counter or trace does not begin with a NuGet download.
RUN dotnet tool install --global dotnet-counters \
 && dotnet tool install --global dotnet-trace \
 && dotnet tool install --global dotnet-dump \
 && dotnet tool install --global dotnet-gcdump

# The checked JIT, discovered and downloaded the same way docs/probes/nightly-checked-jit.sh does
# it, so that DOTNET_JitDump works on the image as shipped. It goes next to the runtime under a
# name of its own, so nothing is overwritten and DOTNET_JitName= is what turns it on.
COPY --chown=vscode:vscode .devcontainer/install-checked-jit.sh /tmp/install-checked-jit.sh
RUN bash /tmp/install-checked-jit.sh && rm /tmp/install-checked-jit.sh

# Warm the NuGet cache and the SDK's first-build state against the repository's own project, so
# that a reader's first build is a build rather than a restore. This is the largest single thing a
# prebuild buys and it is why the image is worth prebuilding at all. The copy is thrown away; only
# the caches it filled are kept.
COPY --chown=vscode:vscode global.json Directory.Build.props ClrXray.slnx /tmp/warm/
COPY --chown=vscode:vscode tools /tmp/warm/tools
RUN cd /tmp/warm \
 && dotnet build ClrXray.slnx -c Release \
 && cd / \
 && rm -rf /tmp/warm
