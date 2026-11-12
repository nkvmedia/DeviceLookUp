# Device Lookup

Windows tool for IT service desks: look up model and warranty for **Lenovo** and **Dell** devices by serial number / service tag. Single lookup, bulk CSV, and barcode scan mode.

## Install (users)

1. Open the latest [Release](https://github.com/nkvmedia/DeviceLookUp/releases/latest).
2. Download `DeviceLookup-win-Setup.exe` (installs for your user, no admin rights) or `DeviceLookup-win-Portable.zip` (unzip anywhere and run).
3. Nothing else needs installing: the .NET runtime is bundled.

The app checks for updates when it starts. When one is available, an **Update** button appears in the sidebar; click it and the app restarts into the new version.

## Dell setup

Dell has no public lookup page apps can use, so Dell devices need a free Dell TechDirect API key:

1. Sign in at tdm.dell.com (business account).
2. Services, Get support and replace parts, APIs, **Request API Key**, project type **Warranty**.
3. When approved, paste the Client ID and Client Secret in **Settings** and press **Test API Connection**.

The secret is stored encrypted (Windows DPAPI) for your Windows account only.

## Releasing a new version (maintainer)

```
git tag v1.2.0
git push origin v1.2.0
```

GitHub Actions builds, tests, packages and publishes the release. Installed apps pick it up automatically.

## Project layout

| Folder | What it is |
| --- | --- |
| `src/DeviceLookup.Core` | All logic: Lenovo and Dell lookups, CSV, cache, view models. No WPF, so it is fully unit tested. |
| `src/DeviceLookup` | The WPF app: theme, screens, updater. |
| `tests/DeviceLookup.Tests` | Test runner. `dotnet run --project tests/DeviceLookup.Tests` |

### Checking the UI against the design

`DeviceLookup.exe --screenshots <folder>` renders every screen with sample data to PNG files. The **Build** workflow does this on every push and attaches them as the `ui-screenshots` artifact.
