# Installer

[`EndHud.wxs`](EndHud.wxs) describes EndHud's Windows installer (MSI), built with
[WiX Toolset](https://wixtoolset.org) v5. Releases are built by
[`.github/workflows/release.yml`](../.github/workflows/release.yml); this is how to build one by hand.

## What it installs

- `EndHud.exe` (self-contained: .NET is included) into `%LocalAppData%\Programs\EndHud`, for the
  current user only, so no administrator prompt.
- A Start menu shortcut, and an entry in Settings → Apps → Installed apps to uninstall it.
- A newer version replaces an installed older one. Installing an older one over a newer one is
  refused with "A newer version of EndHud is already installed."

The installer contains no game art. On first start EndHud downloads the operator avatars and skill
icons from the wiki into `%APPDATA%\EndHud\operators\reference` (about 1 MB), so the first start
needs internet; the portable exe does the same.

Uninstalling removes the program, the shortcut and the installer's registry entry. Your settings,
learned portraits and the downloaded icons in `%APPDATA%\EndHud` are kept.

## Building it by hand

From the repository root, in PowerShell:

```powershell
dotnet tool restore   # WiX, pinned in .config/dotnet-tools.json

dotnet publish src/EndHud -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:Version=1.2.0 -o out/publish

dotnet wix build installer/EndHud.wxs -arch x64 `
  -define "Version=1.2.0" -define "PublishDir=$PWD\out\publish" -o out\EndHud-1.2.0.msi
```

Use `-define`, not `-d`: `dotnet` takes `-d` for itself.

## Rules for changing EndHud.wxs

- Never change the `UpgradeCode` or the two component GUIDs. They are how Windows recognises a new
  MSI as the same program, so it upgrades instead of installing a second copy.
- WiX is pinned to v5. v6 changed its licence (an "open source maintenance fee"), so moving to it
  is a decision, not a routine update.

## Not done yet

- The MSI and exe aren't code-signed, so Windows SmartScreen warns about an unrecognised app on
  first run. Signing needs a code-signing certificate.
