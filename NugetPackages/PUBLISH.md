# NuGet Package Publication Guide

## Packages v1.1.0

All extension packages for Vali-Mediator have been updated to v1.1.0 and are ready for publication.

### Package List

```
Vali-Mediator.AspNetCore.1.1.0.nupkg          (+ .snupkg for symbols)
Vali-Mediator.Resilience.1.1.0.nupkg          (+ .snupkg for symbols)
Vali-Mediator.Caching.1.1.0.nupkg             (+ .snupkg for symbols)
Vali-Mediator.Observability.1.1.0.nupkg       (+ .snupkg for symbols)
Vali-Mediator.Idempotency.1.1.0.nupkg         (+ .snupkg for symbols)
```

### What's New in v1.1.0

**Package structure change:**
- All extension packages now depend on `Vali-Mediator` (v2.0.0+) via NuGet `PackageReference`
- Previously used local `ProjectReference` — converted to independent NuGet packages
- **Fully backward compatible** — no API changes

See `CHANGELOG.md` in project root for details.

---

## Publishing to NuGet.org

### Prerequisites

1. **NuGet API Key**: Get from https://www.nuget.org/account/ApiKeys
2. **dotnet CLI**: Ensure `dotnet` is available on PATH

### Option 1: Using dotnet CLI (Recommended)

```bash
# Set API key (one time)
dotnet nuget add source https://api.nuget.org/v3/index.json --name nuget.org --username __token__ --password <YOUR_API_KEY>

# Publish individual packages
cd NugetPackages/v1.1.0
dotnet nuget push Vali-Mediator.AspNetCore.1.1.0.nupkg -s https://api.nuget.org/v3/index.json -k <YOUR_API_KEY>
dotnet nuget push Vali-Mediator.Resilience.1.1.0.nupkg -s https://api.nuget.org/v3/index.json -k <YOUR_API_KEY>
dotnet nuget push Vali-Mediator.Caching.1.1.0.nupkg -s https://api.nuget.org/v3/index.json -k <YOUR_API_KEY>
dotnet nuget push Vali-Mediator.Observability.1.1.0.nupkg -s https://api.nuget.org/v3/index.json -k <YOUR_API_KEY>
dotnet nuget push Vali-Mediator.Idempotency.1.1.0.nupkg -s https://api.nuget.org/v3/index.json -k <YOUR_API_KEY>

# Publish symbol packages (optional but recommended)
dotnet nuget push Vali-Mediator.AspNetCore.1.1.0.snupkg -s https://api.nuget.org/v3/index.json -k <YOUR_API_KEY>
# ... repeat for other .snupkg files
```

### Option 2: Using Batch Script

**publish.sh** (macOS/Linux):
```bash
#!/bin/bash
API_KEY="<YOUR_API_KEY>"
PACKAGES=(
  "Vali-Mediator.AspNetCore.1.1.0"
  "Vali-Mediator.Resilience.1.1.0"
  "Vali-Mediator.Caching.1.1.0"
  "Vali-Mediator.Observability.1.1.0"
  "Vali-Mediator.Idempotency.1.1.0"
)

for pkg in "${PACKAGES[@]}"; do
  echo "Publishing $pkg..."
  dotnet nuget push "$pkg.nupkg" -s https://api.nuget.org/v3/index.json -k "$API_KEY"
  dotnet nuget push "$pkg.snupkg" -s https://api.nuget.org/v3/index.json -k "$API_KEY"
done
```

**publish.bat** (Windows):
```batch
@echo off
SET API_KEY=<YOUR_API_KEY>

FOR %%P IN (
  "Vali-Mediator.AspNetCore.1.1.0"
  "Vali-Mediator.Resilience.1.1.0"
  "Vali-Mediator.Caching.1.1.0"
  "Vali-Mediator.Observability.1.1.0"
  "Vali-Mediator.Idempotency.1.1.0"
) DO (
  echo Publishing %%P...
  dotnet nuget push "%%P.nupkg" -s https://api.nuget.org/v3/index.json -k %API_KEY%
  dotnet nuget push "%%P.snupkg" -s https://api.nuget.org/v3/index.json -k %API_KEY%
)
```

### Option 3: Web UI

1. Visit https://www.nuget.org/packages/manage/upload
2. Sign in with your NuGet.org account
3. Upload `.nupkg` files one by one
4. Symbol packages (`.snupkg`) upload separately

---

## Verification

After publishing, verify packages appear on NuGet.org:

```
https://www.nuget.org/packages/Vali-Mediator.AspNetCore/1.1.0
https://www.nuget.org/packages/Vali-Mediator.Resilience/1.1.0
https://www.nuget.org/packages/Vali-Mediator.Caching/1.1.0
https://www.nuget.org/packages/Vali-Mediator.Observability/1.1.0
https://www.nuget.org/packages/Vali-Mediator.Idempotency/1.1.0
```

---

## Notes

- ⚠️ **Symbol packages are optional** — `.snupkg` files enable source debugging in Visual Studio
- 📦 **Dependencies**: All packages depend on `Vali-Mediator >= 2.0.0` (must be published first)
- 🔑 **Keep API key secure** — never commit to git or share publicly
- ⏳ **Processing time**: NuGet packages appear in search ~5-10 minutes after publishing
- 🔄 **Updating**: You can only update a package if you own it or are a collaborator

---

Generated: 2026-04-13
Felipe Rafael Montenegro Morriberon
