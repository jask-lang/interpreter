#!/usr/bin/env bash

# exit immediately if a command exits with a non-zero status
set -e

# detect OS
OS_TYPE=$(uname -s)
case "$OS_TYPE" in
    Darwin)
        OS="osx"
        INSTALL_DIR="/usr/local/bin"
        ;;
    Linux)
        OS="linux"
        INSTALL_DIR="/usr/local/bin"
        ;;
    *)
        echo "Error: Unsupported operating system ($OS_TYPE)."
        exit 1
        ;;
esac

# detect arch
ARCH_TYPE=$(uname -m)
case "$ARCH_TYPE" in
    x86_64)
        ARCH="x64"
        ;;
    arm64|aarch64)
        ARCH="arm64"
        ;;
    *)
        echo "Error: Unsupported architecture ($ARCH_TYPE)."
        exit 1
        ;;
esac

if [ "$(id -u)" -ne 0 ]; then
    echo "Error: This script requires root privileges to install to ${INSTALL_DIR}."
    echo "Please run the script using sudo: sudo ./install.sh"
    exit 1
fi

RUNTIME_IDENTIFIER="${OS}-${ARCH}"

echo "Building jask interpreter executable for ${RUNTIME_IDENTIFIER}..."

# build via native AOT into a temporary build output directory
dotnet publish -c Release -r "$RUNTIME_IDENTIFIER" -o ./dist

echo "Build successful. Installing executable to ${INSTALL_DIR}/jask..."

# ensure target directory exists
if [ ! -d "$INSTALL_DIR" ]; then
    sudo mkdir -p "$INSTALL_DIR"
fi

# copy binary to global system path
sudo cp ./dist/jask "${INSTALL_DIR}/jask"

# grant execution permissions
sudo chmod +x "${INSTALL_DIR}/jask"

# remove quarantine attribute on macOS (if applicable)
if [ "$OS" = "osx" ]; then
    sudo xattr -d com.apple.quarantine "${INSTALL_DIR}/jask" 2>/dev/null || true
fi

# clean up build artifacts
rm -rf ./dist

echo "Installation complete. The 'jask' executable is ready for system-wide use!"