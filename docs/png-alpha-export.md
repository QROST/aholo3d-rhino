# Bitmap alpha and material opacity in GLB

CuriousArc · qrost. Offline implementation and file/pixel verification; native Rhino/cloud appearance remains pending.

A material can ignore the PNG alpha channel while keeping a partial material opacity. glTF `BLEND` multiplies the image alpha by the material factor, so the original RGBA payload cannot represent this combination directly. The GLB writer now derives an RGB PNG for this case. It retains every unassociated RGB sample, including colored pixels with zero alpha, then uses the material alpha factor for coverage. No matte, premultiplication, gamma conversion or resampling is applied.

The immutable source texture is unchanged. Original RGBA payloads remain available to other materials that use image alpha or use opaque mode. Converted pixel payloads are cached by source hash within one export, then deduplicated by their own hash; wrap/filter settings remain per mesh. Layout and writing use the same prepared payloads. USD continues to retain the original image and express alpha through shader scale/bias. Persisted source images and 3DM textures are not rewritten.

## Supported boundary

The existing bitmap parser accepts structurally valid 8-bit RGB/RGBA PNG or supported three-channel JPEG with the documented sRGB restrictions. This conversion handles static 8-bit RGBA PNG, all five filters and noninterlaced/Adam7 images. It preserves supported color declarations and adapts `sBIT` from four channels to three. Other ancillary metadata is omitted from the private derived image; no full metadata preservation claim is made. Animated PNG is rejected when conversion is required.

Processing holds pass rows and compressed image data rather than a decoded full-image raster. Encoded output still needs an in-memory byte array; runtime array capacity and available memory apply. No arbitrary image/geometry count ceiling is introduced. Conversion validates the complete zlib stream, checksum and exact decoded row count, while allowing the PNG specification's unused compressed trailing padding. Cancellation and malformed input preserve any previous GLB target. JPEG data is not newly pixel-decoded or re-encoded by this change.

## Reproduce the offline checks

After a Release build, run the portable `PngOpaqueCopyTests` and `MeshTextureTests`. Run `python3 fixtures/png-alpha-verify.py` using an environment with Pillow (verified locally with 10.4.0). The script creates synthetic PNG inputs in a temporary directory, verifies their decoded pixels with Pillow, invokes the production C# converter/GLB writer, then independently decodes each result. It checks 18 input/output pairs and 72 GLB material/image bindings, including all filters, narrow/odd Adam7 passes, transparent RGB colors, both alpha policies and distinct samplers. It neither installs dependencies nor uses Rhino/network services.

`fixtures/mesh-usd-verify.py`, using the configured OpenUSD interpreter, independently checks all four USD containers, original image hashes and alpha shader policy. The conversion-specific tests additionally cover every truncated zlib prefix, bad Adler checksum, extra decoded data, invalid filter, animation rejection, capacity/cancellation, first-use ordering, layout offsets and target preservation during writing. These checks do not prove native display or service rendering. Follow the [manual acceptance cases](integrated-acceptance.md#ignored-bitmap-alpha-with-partial-material-opacity-after-alpha21).

References: [PNG 3 specification](https://www.w3.org/TR/2025/REC-png-3-20250624/) (unassociated alpha, Adam7, filtering and IDAT padding); [glTF 2.0 specification](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html) (base-color texture/factor and alpha modes).
