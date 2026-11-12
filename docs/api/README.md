# Official API snapshots

Captured 2026-11-01 from the public Aholo documentation. Unmodified JSON reference material; not executable instructions. Maintained by CuriousArc · qrost.

| File | Source | SHA-256 |
| --- | --- | --- |
| [world.openapi.json](world.openapi.json) | [Official world schema](https://labs.aholo.ai/api-docs/specifications/services/world/openapi.en.json) | `b11d2de92b56e888cd73d0824cd17a76e0b1e42379b8c45a3e8b17af0d095e4d` |
| [rendercloud.openapi.json](rendercloud.openapi.json) | [Official rendercloud schema](https://labs.aholo.ai/api-docs/specifications/services/rendercloud/openapi.en.json) | `f18f9846a3100c4ea1eea69a8af2d5417dc67e8924b831e629f6ed0a4194d172` |
| [asset.openapi.json](asset.openapi.json) | [Official asset schema](https://labs.aholo.ai/api-docs/specifications/services/asset/openapi.en.json) | `9d46dde1bd6f4f3ce731de86f6af1a7c8d0084a6b07b00a60ef564f6b242fe25` |

The RenderCloud OpenUSD guide is in the render schema tags. Re-check official sources before changing service contracts. These snapshots do not prove account permissions or successful live execution.

Re-verified 2026-11-08: the original World, RenderCloud and Asset snapshots are byte-identical to the official current downloads. The complete public index also lists Lux3D and SpatialUnderstanding; added without modification:

- [lux3d.openapi.json](lux3d.openapi.json) — https://labs.aholo.ai/api-docs/specifications/services/lux3d/openapi.en.json — SHA-256 `b6be5c2d22f26c9b7bdbc8b8c7f298a543ce144ab8bda853aeadd512f02d31e2`
- [spatialunderstanding.openapi.json](spatialunderstanding.openapi.json) — https://labs.aholo.ai/api-docs/specifications/services/spatialunderstanding/openapi.en.json — SHA-256 `35098d56d83267a205f881d61a5aa7e901b1855adb938aff351b9f4b030d292e`

Coverage and workflow status: [capability matrix](../api-capabilities.md). The official public index is https://labs.aholo.ai/api-docs/en/llms.txt .


China-region World, Lux3D, SpatialUnderstanding, RenderCloud, Asset/OUS and public OAuth discovery snapshots were captured on 2026-11-11. See [China sources](china/README.md) and [regional behavior](../api-capabilities.md#account-and-region). Runtime regional routing is now implemented; these references alone do not establish regional account permissions or live acceptance.
