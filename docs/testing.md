# Testing

## Automated checks

```sh
dotnet build Aholo3D.sln -c Release --no-restore
dotnet test Aholo3D.sln -c Release --no-build
python3 scripts/test-product-rename.py
python3 scripts/check-region-contracts.py
python3 fixtures/oauth-coordinator-verify.py
python3 fixtures/mesh-shading-verify.py --geometry-only
python3 fixtures/gaussian-antialias-verify.py
python3 fixtures/create-textured-3mf.py --check
python3 scripts/create-gs-assets.py --check
python3 scripts/check-public-release.py
python3 scripts/test-public-release.py
```

Tests use synthetic data and mock service transports. They verify contracts and implementation behavior; they do not prove live entitlements, cloud image quality or native Rhino appearance.

## Native checks

Run on macOS and Windows, in English and Chinese:

1. Open Create, Library, Render and Settings at narrow and wide widths. Check scrolling, keyboard focus, primary buttons and validation in every creation form.
2. Import a colored GS sample and a textured mesh. Check navigation, opaque geometry interaction, clipping, placement, Undo/Redo and save/reopen.
3. Exercise supported local USD/USDZ mesh, GS and mixed-scene roundtrips. Check physical units, materials, cancellation and missing dependencies.
4. Search/filter rapidly; refresh/download existing tasks without creating new ones. Switch documents or close the panel during preparation.
5. Opt into a saved document's draft memory, restart, then Forget and restart again. Check separate documents, Save As and conflicting sessions.
6. Upgrade from a legacy installation and verify only the new package loads while old documents and assets remain accessible.

Use actual source/package versions when recording results. Existing macOS feedback does not automatically establish a newer UI or Windows result.

## Live service checks

Use explicitly chosen, non-sensitive inputs and a provider account authorized for the operation. Test one World, one Lux3D mesh, one Spatial operation, offline rendering and live preview as applicable. Check original IDs and credit use before retrying anything uncertain.

The current preview still requires native acceptance of its latest UI and Windows workflows. Native cloud depth compositing and animation playback are outside its supported scope.
