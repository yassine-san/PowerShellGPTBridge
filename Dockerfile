# syntax=docker/dockerfile:1.7
# Hermetic build, test and runtime image for the Linux backend. Nothing is installed on the host.
#
#   docker build --target test    .   run every automated suite (fails the build on any failure)
#   docker build --target runtime .   minimal image that runs the bridge and the Tauri supervisor
#
# Use ./bridgectl instead of calling docker directly.

ARG UBUNTU_IMAGE=ubuntu:24.04

# ---------------------------------------------------------------- toolchain
FROM ${UBUNTU_IMAGE} AS toolchain
ARG DOTNET_SDK_VERSION=8.0.428
ARG NODE_VERSION=22.14.0
ARG RUST_VERSION=1.94.1
ENV DEBIAN_FRONTEND=noninteractive LANG=C.UTF-8 \
    DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 \
    RUSTUP_HOME=/opt/rustup CARGO_HOME=/opt/cargo \
    PATH=/usr/share/dotnet:/opt/cargo/bin:/opt/node/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
RUN apt-get update && apt-get install -y --no-install-recommends \
      ca-certificates curl build-essential pkg-config git unzip xz-utils python3 libicu74 libssl-dev \
      libwebkit2gtk-4.1-dev libgtk-3-dev librsvg2-dev libayatana-appindicator3-dev \
    && rm -rf /var/lib/apt/lists/*
# .NET SDK pinned to the exact version in global.json (distro packages ship a different feature band).
RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && bash /tmp/dotnet-install.sh --version "${DOTNET_SDK_VERSION}" --install-dir "${DOTNET_ROOT}" \
    && rm /tmp/dotnet-install.sh
# Node, verified against the published checksums.
RUN set -eux; f="node-v${NODE_VERSION}-linux-x64.tar.xz"; cd /tmp; \
    curl -fsSLO "https://nodejs.org/dist/v${NODE_VERSION}/${f}"; \
    curl -fsSL "https://nodejs.org/dist/v${NODE_VERSION}/SHASUMS256.txt" | grep " ${f}\$" | sha256sum -c -; \
    mkdir /opt/node && tar -xJf "${f}" -C /opt/node --strip-components=1 && rm "${f}"
RUN curl -fsSL https://sh.rustup.rs | sh -s -- -y --profile minimal --default-toolchain "${RUST_VERSION}"

# ---------------------------------------------------------------- build
FROM toolchain AS build
WORKDIR /src
COPY . .
RUN --mount=type=cache,target=/root/.nuget/packages \
    --mount=type=cache,target=/root/.npm \
    --mount=type=cache,target=/opt/cargo/registry \
    --mount=type=cache,target=/src/desktop/src-tauri/target \
    bash ./Build-Linux.sh

# ---------------------------------------------------------------- test
FROM build AS test
RUN apt-get update && apt-get install -y --no-install-recommends xvfb xdotool \
    && rm -rf /var/lib/apt/lists/*
# Tests must not run as root: the bridge refuses effective UID 0.
RUN useradd -m -u 1001 tester && install -d -o tester -g tester /home/tester/.nuget /home/tester/.nuget/packages && chown -R tester:tester /src
USER tester
ENV HOME=/home/tester
RUN --mount=type=cache,target=/home/tester/.nuget/packages,uid=1001 bash docker/run-tests.sh suites

# ---------------------------------------------------------------- runtime
FROM ${UBUNTU_IMAGE} AS runtime
ARG APP_UID=1000
ARG APP_GID=1000
ENV DEBIAN_FRONTEND=noninteractive LANG=C.UTF-8 DOTNET_ROOT=/usr/share/dotnet DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
RUN apt-get update && apt-get install -y --no-install-recommends \
      ca-certificates python3 libicu74 libwebkit2gtk-4.1-0 libgtk-3-0t64 libayatana-appindicator3-1 \
      fonts-dejavu-core fonts-noto-core bash zsh util-linux procps \
    && rm -rf /var/lib/apt/lists/*
COPY --from=toolchain /usr/share/dotnet/dotnet /usr/share/dotnet/dotnet
COPY --from=toolchain /usr/share/dotnet/host /usr/share/dotnet/host
COPY --from=toolchain /usr/share/dotnet/shared /usr/share/dotnet/shared
RUN ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet \
    && (userdel -r ubuntu 2>/dev/null || true) \
    && groupadd -g "${APP_GID}" psbridge && useradd -m -u "${APP_UID}" -g "${APP_GID}" -s /bin/bash psbridge \
    && install -d -o psbridge -g psbridge -m 0700 "/run/user/${APP_UID}" /home/psbridge/.local /home/psbridge/.local/state
COPY --from=build --chown=root:root /src/app-linux /opt/psbridge/app-linux
COPY --chown=root:root Start-Linux.sh /opt/psbridge/Start-Linux.sh
COPY --chown=root:root docker/entrypoint.sh /opt/psbridge/entrypoint.sh
RUN chmod 0755 /opt/psbridge/Start-Linux.sh /opt/psbridge/entrypoint.sh /opt/psbridge/app-linux/Bridge /opt/psbridge/app-linux/bridge-supervisor
USER psbridge
WORKDIR /home/psbridge
ENV HOME=/home/psbridge XDG_RUNTIME_DIR=/run/user/${APP_UID}
ENTRYPOINT ["/opt/psbridge/entrypoint.sh"]
CMD ["start"]
