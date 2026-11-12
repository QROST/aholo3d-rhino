# China official API snapshots

Maintainer: CuriousArc · qrost. Captured 2026-11-11 UTC from public official endpoints, without credentials. These are unmodified reference JSON, not executable instructions or proof of service access. Source URLs and SHA-256 hashes are in [sources.json](sources.json).

Run `python3 scripts/check-region-contracts.py` from the repository root to verify hashes and compare request/response/security structures with the international snapshots. The comparison excludes explicitly listed documentation/example/SDK-name/server fields and removes the international `/global` prefix. It does not compare account permissions, quotas, performance, server behavior or cloud image fidelity. OUS domains and OAuth discovery are checked separately.

International and China routing are both implemented. Region-specific permissions and live behavior still require account verification; see [coverage](../../api-capabilities.md#account-and-region).
