# NuGet publication guide — release 3.0

Packages: `Vali-Mediator` **3.0.0** (core) and `Vali-Mediator.AspNetCore|Caching|Idempotency|Observability|Resilience` **2.0.0**.

## Order (mandatory)

1. Publish the core `Vali-Mediator 3.0.0` first and wait until it is indexed (5–10 min).
2. Publish the five extension packages. Their `.nuspec` depends on `Vali-Mediator >= 3.0.0`; recommend consumers the range `[3.0.0, 4.0.0)`.

An extension package built against core 2.x fails at run time with `MissingMethodException` on core 3.x, so never publish extensions before the core.

## Build the packages

```bash
dotnet test Vali-Mediator.sln -c Release
for p in Vali-Mediator Vali-Mediator.AspNetCore Vali-Mediator.Caching Vali-Mediator.Idempotency Vali-Mediator.Observability Vali-Mediator.Resilience; do
  dotnet pack "$p/$p.csproj" -c Release -o ./artifacts
done
```

The manual `Release (pack only)` GitHub workflow produces the same artifacts. It never pushes to NuGet.

## Publish

Read the API key from an environment variable; never write it in files or commit it.

```bash
dotnet nuget push "artifacts/Vali-Mediator.3.0.0.nupkg" -s https://api.nuget.org/v3/index.json -k "$NUGET_API_KEY"
# after the core is indexed:
dotnet nuget push "artifacts/Vali-Mediator.*.2.0.0.nupkg" -s https://api.nuget.org/v3/index.json -k "$NUGET_API_KEY"
```

## Verify

Check each package page (`https://www.nuget.org/packages/<PackageId>/<version>`), and confirm in a scratch project that a fresh `dotnet add package` of an extension resolves core 3.x.

## Notes

- Release notes live in `CHANGELOG.md`; the migration guide is `docs/MIGRACION-3.0.md`.
- Versions are set in each `.csproj`; shared metadata (authors, license, SourceLink, symbols) is in `Directory.Build.props`.
- API-compat validation against the 2.0.1 baseline was not enabled because 3.0 is intentionally breaking; enable `PackageValidationBaselineVersion` = 3.0.0 for the 3.x line once it ships.
