# The image every CI and release job runs in (ghcr.io/openigi/ci). Built by .github/workflows/ci-image.yml
# whenever this file changes.
#
# One image, so the self-hosted runner and GitHub's hosted runners build in the same environment: the
# .NET SDK the repository's global.json accepts, the browser-wasm workload the web build needs, and the
# release tools the workflows call. Pinned versions, so a rebuild of the image is a rebuild of the same one.
FROM mcr.microsoft.com/dotnet/sdk:10.0

ARG GIT_CLIFF_VERSION=2.14.2
ARG RCODESIGN_VERSION=0.29.0
ARG GH_VERSION=2.102.0

# zip for the Windows archives; openssh-client and rsync for the VM deploys; libopenal1 because the
# OpenAL backend's tests load it; jq for the workflows' small JSON reads.
RUN apt-get update \
 && apt-get install -y --no-install-recommends zip unzip xz-utils jq openssh-client rsync libopenal1 python3 \
 && rm -rf /var/lib/apt/lists/*

# The web build compiles native code (WebGPU bridge, AOT) through Emscripten.
RUN dotnet workload install wasm-tools --skip-manifest-update \
 && rm -rf /root/.nuget/packages /tmp/*

# git-cliff: release notes and the next version, from the commit history (cliff.toml).
RUN curl -fsSL "https://github.com/orhun/git-cliff/releases/download/v${GIT_CLIFF_VERSION}/git-cliff-${GIT_CLIFF_VERSION}-x86_64-unknown-linux-musl.tar.gz" \
    | tar -xz -C /tmp \
 && install -m 0755 "/tmp/git-cliff-${GIT_CLIFF_VERSION}/git-cliff" /usr/local/bin/git-cliff \
 && rm -rf /tmp/git-cliff-*

# rcodesign: the ad-hoc signature Apple silicon requires, applied from Linux (tools/release/package.sh).
RUN curl -fsSL "https://github.com/indygreg/apple-platform-rs/releases/download/apple-codesign%2F${RCODESIGN_VERSION}/apple-codesign-${RCODESIGN_VERSION}-x86_64-unknown-linux-musl.tar.gz" \
    | tar -xz -C /tmp \
 && install -m 0755 /tmp/apple-codesign-*/rcodesign /usr/local/bin/rcodesign \
 && rm -rf /tmp/apple-codesign-*

# gh: creating the GitHub release.
RUN curl -fsSL "https://github.com/cli/cli/releases/download/v${GH_VERSION}/gh_${GH_VERSION}_linux_amd64.tar.gz" \
    | tar -xz -C /tmp \
 && install -m 0755 "/tmp/gh_${GH_VERSION}_linux_amd64/bin/gh" /usr/local/bin/gh \
 && rm -rf /tmp/gh_*

# Checkouts are mounted from the runner and owned by another uid.
RUN git config --system --add safe.directory '*'

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
