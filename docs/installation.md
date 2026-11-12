# Installation and upgrades

## Released packages

Use `_PackageManager` in Rhino 8.32+ and search for `aholo3d-rhino`. Enable prereleases for preview versions. Choose the package for your platform, restart Rhino and run `Aholo3DPanel`.

Install the .NET 8 runtime for your system if the background service cannot start. Rhino supplies RhinoCommon and Eto; do not replace Rhino's copies with downloaded DLLs.

## Local Yak files

After downloading a matching release asset, use the Yak executable shipped with Rhino:

macOS:
```sh
"/Applications/Rhino 8.app/Contents/Resources/bin/yak" install "/path/to/package.yak"
```

Windows PowerShell:
```powershell
& "C:\Program Files\Rhino 8\System\Yak.exe" install "C:\path\to\package.yak"
```

Replace the example path with the actual package, then restart Rhino. Developers can use `scripts/install-local.py` to verify a locally built archive and its checksum before installation.

## Upgrading from aholo-rhino

The old and new names share the same plugin GUID. Uninstall **aholo-rhino** in PackageManager, close Rhino, then install **aholo3d-rhino**. Do not delete the application-data directory or move cached assets that saved 3DMs still reference.

The local installer backs up a legacy package outside Rhino's package scan before replacing it and restores it if installation fails. Existing task records, document markers, credentials and legacy command names remain compatible. A legacy data directory may remain in use or be reached through a new-name directory link; this preserves existing absolute references.

The old package is unlisted from Yak. Yak unlisting hides it from search; an exact old version can still be downloaded. It does not uninstall copies on other computers. [Yak reference](https://developer.rhino3d.com/guides/yak/yak-cli-reference/#yank).

## Optional USD runtime

Open **Settings → Uploads and USD tools → Check USD readiness**. Follow the displayed setup command only if you need local USD/USDZ exchange. Setup installs the pinned OpenUSD runtime separately; the plugin package includes bridge scripts, not that runtime.

## Uninstall

Remove `aholo3d-rhino` in PackageManager and restart. Cached models and task records are separate from the plugin files. Keep them while documents reference them; see [data locations](privacy.md).
