# Development

## Build

Use .NET 8 SDK. Rhino is needed for native testing, not for the portable tests.

```sh
dotnet restore Aholo3D.sln
dotnet build Aholo3D.sln -c Release --no-restore
dotnet test Aholo3D.sln -c Release --no-build
python3 scripts/check-public-release.py
```

The workflow in `.github/workflows/ci.yml` runs Mac and Windows builds plus contract, geometry, credential-coordination and package checks. OpenUSD fixtures require the separately installed pinned runtime when testing that integration.

## Structure

| Project | Responsibility |
| --- | --- |
| `Aholo3D.Core` | Contracts, local journal, workflow policies, format readers and Gaussian math |
| `Aholo3D.Provider` | HTTP/WebSocket protocols, uploads and credential protection |
| `Aholo3D.Sidecar` | .NET 8 background process and private pipe dispatch |
| `Aholo3D.Rhino` | Eto UI, native geometry, display and document integration |
| `Aholo3D.Tests` | Portable contracts and regression tests |

Core/Rhino target the runtime-compatible framework used by Rhino; Provider/Sidecar run on .NET 8. Only Rhino's UI thread mutates a document. Long preparation uses captured data and checks document/account identity again before committing results.

Cloud creations record intent before submission. Unknown replies do not authorize retry. Read-only history caches do not replace authoritative task reads. Credentials and signed URLs must not enter journals, logs or reusable UI drafts.

## Package locally

After successful Release checks:

```sh
python3 scripts/package.py --platform mac --yak "/Applications/Rhino 8.app/Contents/Resources/bin/yak" --checks passed
python3 scripts/package.py --platform win --yak "/Applications/Rhino 8.app/Contents/Resources/bin/yak" --checks passed
```

Windows can use its installed Yak executable. `--stage-only` verifies the payload without Yak. Package receipts and other generated files stay in ignored `artifacts/`. Building an archive does not publish it.

Use `scripts/install-local.py` with the verified archive to install a local candidate. The installer never launches Rhino. Test the native workflows separately.

## Contracts and changes

[API snapshots](https://github.com/QROST/aholo3d-rhino/tree/main/docs/api) are versioned reference material. Keep their source hashes and third-party notices. Do not infer account access from mock responses.

Maintain compatibility with existing plugin/command GUIDs, task identities, document markers and credential stores. Changes to UI text should include English and Chinese. Keep tests focused on observable behavior, invalid inputs and cancellation/ownership boundaries.

[Contributing](../CONTRIBUTING.md) · [Testing](testing.md)
