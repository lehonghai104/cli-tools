# Tools.slnx - build & deploy the CLI tools in src/ for the current OS/architecture.
#
#   make              build + test the whole solution
#   make tools        list the tools that can be installed
#   make publish      native binary for this machine -> dist/<tool>/<rid>/
#   make install      publish + install to the user's bin dir (no sudo needed)
#   make uninstall    remove installed files (user config such as ~/.totp is kept)
#   make tool-install install as a .NET global tool instead (needs the .NET runtime)
#   make info         show detected OS / RID / install paths
#
# Pick the tool with TOOL=<project name in src/> (default: Totp):
#   make install TOOL=Totp
#
# Overrides:
#   make publish RID=linux-arm64       target another RID (native AOT can only target the host OS)
#   make publish AOT=0                 self-contained single file, no C/C++ toolchain needed
#   make install PREFIX=/usr/local     (unix; may need sudo)

SOLUTION      := Tools.slnx
TOOL          ?= Totp
PROJECT       := src/$(TOOL)/$(TOOL).csproj
# Command name = lowercase project name (matches AssemblyName in Directory.Build.props).
lc             = $(subst A,a,$(subst B,b,$(subst C,c,$(subst D,d,$(subst E,e,$(subst F,f,$(subst G,g,$(subst H,h,$(subst I,i,$(subst J,j,$(subst K,k,$(subst L,l,$(subst M,m,$(subst N,n,$(subst O,o,$(subst P,p,$(subst Q,q,$(subst R,r,$(subst S,s,$(subst T,t,$(subst U,u,$(subst V,v,$(subst W,w,$(subst X,x,$(subst Y,y,$(subst Z,z,$1))))))))))))))))))))))))))
CMD           := $(call lc,$(TOOL))
PACKAGE_ID    := $(TOOL).Cli
CONFIGURATION ?= Release
AOT           ?= 1
DOTNET        ?= dotnet

# ---------------------------------------------------------------- OS / arch detection
ifeq ($(OS),Windows_NT)
  HOST_OS := windows
  ifeq ($(PROCESSOR_ARCHITECTURE),ARM64)
    HOST_ARCH := arm64
  else ifeq ($(PROCESSOR_ARCHITEW6432),ARM64)
    HOST_ARCH := arm64
  else
    HOST_ARCH := x64
  endif
  RID_OS := win
  EXE    := .exe
else
  UNAME_S := $(shell uname -s)
  UNAME_M := $(shell uname -m)
  ifeq ($(UNAME_S),Darwin)
    HOST_OS := macos
    RID_OS  := osx
  else ifeq ($(UNAME_S),Linux)
    HOST_OS := linux
    # Alpine & co. use musl libc and need the linux-musl-* RIDs.
    ifneq ($(shell ldd --version 2>&1 | grep -i musl),)
      RID_OS := linux-musl
    else
      RID_OS := linux
    endif
  else
    $(error Unsupported OS '$(UNAME_S)'. Supported: macOS, Linux, Windows)
  endif
  ifneq ($(filter $(UNAME_M),arm64 aarch64),)
    HOST_ARCH := arm64
  else ifneq ($(filter $(UNAME_M),x86_64 amd64),)
    HOST_ARCH := x64
  else
    $(error Unsupported CPU architecture '$(UNAME_M)'. Supported: x64, arm64)
  endif
  EXE :=
endif

RID      ?= $(RID_OS)-$(HOST_ARCH)
DIST_DIR := dist/$(CMD)/$(RID)

ifeq ($(AOT),1)
  PUBLISH_FLAGS := -p:PublishAot=true
else
  PUBLISH_FLAGS := --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=true -p:IncludeNativeLibrariesForSelfExtract=true
endif

# ---------------------------------------------------------------- install locations
ifeq ($(HOST_OS),windows)
  # %LOCALAPPDATA%\Programs\<cmd>, added to the user PATH by scripts/install.ps1
  INSTALL_DIR ?= $(LOCALAPPDATA)\Programs\$(CMD)
  PS      := powershell -NoProfile -ExecutionPolicy Bypass -Command
  PS_FILE := powershell -NoProfile -ExecutionPolicy Bypass -File
else
  PREFIX      ?= $(HOME)/.local
  INSTALL_DIR ?= $(PREFIX)/lib/$(CMD)
  BIN_DIR     ?= $(PREFIX)/bin
endif

.DEFAULT_GOAL := all
.PHONY: all build test tools check-tool publish install uninstall tool-install tool-uninstall pack info clean

all: build test

info:
	@echo "Tool         : $(TOOL) (command: $(CMD))"
	@echo "Host OS      : $(HOST_OS) ($(HOST_ARCH))"
	@echo "RID          : $(RID)"
	@echo "Native AOT   : $(AOT)"
	@echo "Publish dir  : $(DIST_DIR)"
	@echo "Install dir  : $(INSTALL_DIR)"
ifneq ($(HOST_OS),windows)
	@echo "Bin dir      : $(BIN_DIR)"
endif

build:
	$(DOTNET) build $(SOLUTION) -c $(CONFIGURATION)

test:
	$(DOTNET) test $(SOLUTION)

tools:
	@echo "Tools in src/ (use TOOL=<name>):"
	@$(foreach t,$(notdir $(patsubst %/,%,$(dir $(wildcard src/*/*.csproj)))),echo "  $(t)  -> command: $(call lc,$(t))";)

check-tool:
	$(if $(wildcard $(PROJECT)),,$(error No project at $(PROJECT). Run 'make tools' to see available tools))

publish: check-tool
	@echo "==> Publishing $(CMD) for $(RID) (AOT=$(AOT))"
ifeq ($(HOST_OS),windows)
	-@$(PS) "Remove-Item -Recurse -Force -ErrorAction SilentlyContinue '$(DIST_DIR)'"
else
	@rm -rf "$(DIST_DIR)"
endif
	$(DOTNET) publish $(PROJECT) -c $(CONFIGURATION) -r $(RID) $(PUBLISH_FLAGS) -o $(DIST_DIR)
	@echo "==> Output: $(DIST_DIR)/$(CMD)$(EXE)"

ifeq ($(HOST_OS),windows)

install: publish
	$(PS_FILE) scripts/install.ps1 -Name $(CMD) -Source "$(DIST_DIR)" -Dest "$(INSTALL_DIR)"

uninstall:
	$(PS_FILE) scripts/uninstall.ps1 -Name $(CMD) -Dest "$(INSTALL_DIR)"

clean:
	-$(PS) "Remove-Item -Recurse -Force -ErrorAction SilentlyContinue dist, nupkg; Get-ChildItem src, tests -Directory | ForEach-Object { Remove-Item -Recurse -Force -ErrorAction SilentlyContinue (Join-Path $$_.FullName 'bin'), (Join-Path $$_.FullName 'obj') }"

else

install: publish
	@mkdir -p "$(INSTALL_DIR)" "$(BIN_DIR)"
	@rm -rf "$(INSTALL_DIR)"/*
	@# Binary + native libs (libSkiaSharp) stay together; only a symlink goes on PATH.
	@find "$(DIST_DIR)" -mindepth 1 -maxdepth 1 ! -name '*.dbg' ! -name '*.dSYM' ! -name '*.pdb' -exec cp -R {} "$(INSTALL_DIR)/" \;
	@chmod +x "$(INSTALL_DIR)/$(CMD)"
	@ln -sf "$(INSTALL_DIR)/$(CMD)" "$(BIN_DIR)/$(CMD)"
ifeq ($(HOST_OS),macos)
	@# Locally built binaries aren't quarantined, but clear it in case the files were copied in.
	@xattr -dr com.apple.quarantine "$(INSTALL_DIR)" 2>/dev/null || true
endif
	@echo "==> Installed $(INSTALL_DIR)/$(CMD)"
	@echo "==> Linked    $(BIN_DIR)/$(CMD)"
	@case ":$$PATH:" in *":$(BIN_DIR):"*) ;; \
	  *) echo ""; echo "NOTE: $(BIN_DIR) is not on your PATH. Add this to your shell profile:"; \
	     echo "  export PATH=\"$(BIN_DIR):\$$PATH\"";; esac

uninstall:
	rm -f "$(BIN_DIR)/$(CMD)"
	rm -rf "$(INSTALL_DIR)"
	@echo "==> Uninstalled $(CMD) (user config files were kept)"

clean:
	rm -rf dist nupkg src/*/bin src/*/obj tests/*/bin tests/*/obj

endif

# ---------------------------------------------------------------- .NET global tool (any OS)
pack: check-tool
	$(DOTNET) pack $(PROJECT) -c $(CONFIGURATION)

tool-install: pack
	-$(DOTNET) tool uninstall -g $(PACKAGE_ID)
	$(DOTNET) tool install -g $(PACKAGE_ID) --add-source ./nupkg

tool-uninstall:
	$(DOTNET) tool uninstall -g $(PACKAGE_ID)
