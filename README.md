# totp

A small cross-platform (.NET 10) CLI that generates time-based one-time passwords (RFC 6238), compatible with Google Authenticator, Authy, etc.

```sh
$ totp add ~/Downloads/github-2fa.png
Added 'GitHub' (alice@example.com).
$ totp get github
493985
```

## Commands

| Command | Description |
|---|---|
| `totp list` | List apps: number (for `totp get <index>`), name, account. |
| `totp list --show` | Also show each app's current code and seconds until it expires. |
| `totp get` | Show a numbered menu of apps, pick one by number (or name), print its code. |
| `totp get <app\|index>` | Print **only** the code, for use in other commands. Case-insensitive; a unique prefix works (`totp get git`). |
| `totp add <qr-image> [name]` | Read a QR code image (or an `otpauth://` link) and store its secret. See below. |
| `totp add <app> <secret> [-d 6] [-p 30] [-a SHA1] [-f]` | Add an app from a Base32 secret (spaces and lowercase are fine). `-f` overwrites. |
| `totp remove <app>` | Remove an app. |

Run `totp` with no arguments to see help and the config file path.

## Using the code in other commands

`totp get <app>` writes just the digits to stdout. It adds no newline when the output is piped or captured. Errors go to stderr with exit code 1, so a failure never ends up inside the other command.

```sh
vpn connect --otp "$(totp get aws)"
totp get github | pbcopy                 # macOS clipboard, digits only
totp get github | clip                   # Windows
some-cli login --code $(totp get "GitHub 2")
sed 's/\$OTP/'"$(totp get aws)"'/g' input.txt > output.txt
```

## Adding from a QR code

```sh
$ totp add ~/Downloads/github-2fa.png
Added 'GitHub' (alice@example.com).
```

- Reads PNG, JPEG, WebP, GIF and BMP. Screenshots work, including transparent backgrounds and several QR codes in one image.
- The app name comes from the QR code's issuer (for example `GitHub`). If that name is taken, it becomes `GitHub:<account>`, then `GitHub 2`. Pass a second argument to choose the name: `totp add qr.png Work`.
- A secret that's already stored is skipped.
- **Google Authenticator export**: in Google Authenticator, use *Transfer accounts → Export* and screenshot the QR code. All accounts in it are imported in one go.
- You can pass an `otpauth://...` link instead of an image path.
- `add` treats its first argument as a QR image when there's only one argument, or when it's an existing file or an `otpauth://` link. Otherwise it reads `<name> <secret>`.
- Only TOTP is supported. HOTP (counter-based) entries are skipped.

## Config file

Default location: `~/.totp/config.json` (Windows: `%USERPROFILE%\.totp\config.json`). Set `TOTP_CONFIG` to use another path. If the file (or its folder) doesn't exist, it's created empty the first time you run `totp`.

```json
{
  "apps": [
    { "name": "GitHub", "account": "alice@example.com", "secret": "JBSWY3DPEHPK3PXP", "digits": 6, "period": 30, "algorithm": "SHA1" }
  ]
}
```

You can edit it by hand; `account`, `digits`, `period` and `algorithm` are optional. On macOS/Linux the file is set to `600` (owner-only) when the CLI writes it.

> ⚠️ Secrets are stored in plain text. Keep the file private and out of synced/backed-up folders you don't trust.

## Install

Requires the .NET 10 SDK. The Makefile detects your OS (macOS, Linux glibc or musl, Windows) and CPU (x64, arm64), then builds and installs a native binary for it:

```sh
make info        # show what was detected
make install     # build and install
make uninstall   # remove (keeps ~/.totp/config.json)
```

| OS | Installed to | On PATH via |
|---|---|---|
| macOS / Linux | `~/.local/lib/totp/` | symlink `~/.local/bin/totp` (it prints a hint if that folder isn't on your `PATH`) |
| Windows | `%LOCALAPPDATA%\Programs\totp\` | added to your user `PATH` (open a new terminal afterwards) |

Options:

```sh
make install PREFIX=/usr/local     # system-wide on macOS/Linux (may need sudo)
make install AOT=0                 # no C/C++ toolchain needed (see below)
make publish RID=linux-arm64       # build only, for a specific target, into dist/totp/<target>/
make tool-install                  # install as a .NET global tool instead (needs the .NET runtime)
```

**Native AOT** (the default) produces a fast-starting ~3 MB binary plus `libSkiaSharp`, but it needs a native toolchain:

- macOS: Xcode Command Line Tools (`xcode-select --install`)
- Linux: `clang` and `zlib1g-dev` (Debian/Ubuntu), or `clang` and `zlib-dev` (Alpine)
- Windows: Visual Studio with the "Desktop development with C++" workload

If you don't have one, use `AOT=0`. That builds a trimmed, self-contained single file (~30 MB) with no extra tools needed.

On Windows, run `make` from Git Bash, MSYS2, or any shell with GNU Make (e.g. `winget install GnuWin32.Make`). The install steps run through `scripts/install.ps1` and `scripts/uninstall.ps1`, which you can also run directly.

## Test

```sh
make test        # or: dotnet test Tools.slnx
```

Tests include the RFC 6238 Appendix B vectors for SHA1/SHA256/SHA512, QR images round-tripped through PNG and JPEG, and Google Authenticator export parsing.

## Dependencies

- [ZXing.Net](https://github.com/micjahn/ZXing.Net) (Apache-2.0) decodes the QR codes.
- [SkiaSharp](https://github.com/mono/SkiaSharp) (MIT) loads the images. It ships native builds for Windows, macOS and Linux; the Linux one has no extra system dependencies.

## Repository layout

This repo is a solution of CLI tools (`Tools.slnx`); `totp` is the first one.

```
Tools.slnx                 solution with every tool and its tests
Directory.Build.props      shared settings: net10.0, nullable, AOT, packaging as a .NET tool
Makefile                   build / test / publish / install, per tool (TOOL=<Name>)
scripts/                   Windows install/uninstall helpers used by the Makefile
src/<Tool>/                one folder per tool (src/Totp)
tests/<Tool>.Tests/        its tests (tests/Totp.Tests)
```

### Adding a new tool

```sh
dotnet new console -n Foo -o src/Foo
dotnet new xunit   -n Foo.Tests -o tests/Foo.Tests
dotnet add tests/Foo.Tests reference src/Foo/Foo.csproj
dotnet sln Tools.slnx add src/Foo tests/Foo.Tests
```

Then remove `TargetFramework`, `ImplicitUsings` and `Nullable` from the two new `.csproj` files, since `Directory.Build.props` already sets them. From `Directory.Build.props`, a project in `src/Foo` automatically gets the command name `foo` (the project name in lowercase), the .NET tool package `Foo.Cli`, and AOT support.

```sh
make tools               # list installable tools
make install TOOL=Foo    # installs the `foo` command
```

