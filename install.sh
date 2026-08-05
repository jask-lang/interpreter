#!/usr/bin/env bash

# exit immediately if a command exits with a non-zero status
set -e

# detect OS
OS_TYPE=$(uname -s)
case "$OS_TYPE" in
    Darwin)
        OS="osx"
        INSTALL_DIR="$HOME/Library/Application Support/jask/bin"
        ;;
    Linux)
        OS="linux"
        INSTALL_DIR="$HOME/.local/bin"
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

RUNTIME_IDENTIFIER="${OS}-${ARCH}"

echo "Building jask interpreter executable for ${RUNTIME_IDENTIFIER}..."

# build via native AOT into a temporary build output directory
dotnet publish -c Release -r "$RUNTIME_IDENTIFIER" -o ./dist

echo "Build successful. Installing executable to ${INSTALL_DIR}/jask..."

# ensure target directory exists
mkdir -p "$INSTALL_DIR"

# copy binary to user bin directory
cp ./dist/jask "${INSTALL_DIR}/jask"

# grant execution permissions
chmod +x "${INSTALL_DIR}/jask"

# remove quarantine attribute on macOS (if applicable)
if [ "$OS" = "osx" ]; then
    xattr -d com.apple.quarantine "${INSTALL_DIR}/jask" 2>/dev/null || true
fi

# clean up build artifacts
rm -rf ./dist

# add to user PATH if not already present
SHELL_RC=""
if [ -f "$HOME/.zshrc" ]; then
    SHELL_RC="$HOME/.zshrc"
elif [ -f "$HOME/.bashrc" ]; then
    SHELL_RC="$HOME/.bashrc"
elif [ -f "$HOME/.bash_profile" ]; then
    SHELL_RC="$HOME/.bash_profile"
fi

if [ -n "$SHELL_RC" ]; then
    if grep -qF "$INSTALL_DIR" "$SHELL_RC" 2>/dev/null; then
        echo "$INSTALL_DIR is already in your PATH."
    else
        echo "export PATH=\"$INSTALL_DIR:\$PATH\"" >> "$SHELL_RC"
        echo "Added $INSTALL_DIR to your PATH in $(basename "$SHELL_RC")."
    fi
else
    echo ""
    echo "Note: $INSTALL_DIR was not found in your shell config."
    echo "Please add it to your PATH manually by adding the following line"
    echo "to your shell config (~/.zshrc, ~/.bashrc, or ~/.bash_profile):"
    echo ""
    echo "  export PATH=\"$INSTALL_DIR:\$PATH\""
fi

echo ""
echo "Installation complete. The 'jask' executable is ready for use!"
