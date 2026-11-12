#!/usr/bin/env python3
"""Check committed regional API snapshots offline; does not establish live entitlement or execution."""
import hashlib
import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
API = ROOT / "docs/api"
SERVICES = ("world", "lux3d", "spatialunderstanding", "rendercloud", "asset")
# Comparison is limited to request/response/security shapes. Prose, examples,
# generated SDK operation names and environment addresses are compared separately.
DOCUMENTATION = {"description", "summary", "title", "example", "examples", "operationId", "servers", "externalDocs", "x-codeSamples"}

def normalize(value):
    if isinstance(value, dict):
        return {key: normalize(item) for key, item in value.items() if key not in DOCUMENTATION}
    if isinstance(value, list):
        return [normalize(item) for item in value]
    return value

def shape(value):
    paths = {}
    for path, operation in value["paths"].items():
        normalized = path[len("/global"):] if path.startswith("/global/") else path
        if normalized in paths:
            raise ValueError("Ambiguous normalized API path")
        paths[normalized] = operation
    return normalize({"paths": paths, "components": value.get("components", {})})

def check():
    inventory = json.loads((API / "china/sources.json").read_text(encoding="utf-8"))
    for entry in inventory["sources"]:
        path = API / "china" / entry["file"]
        if hashlib.sha256(path.read_bytes()).hexdigest() != entry["sha256"]:
            raise ValueError("Official snapshot bytes changed: " + entry["file"])
    for name in SERVICES:
        international = json.loads((API / (name + ".openapi.json")).read_text(encoding="utf-8"))
        china = json.loads((API / "china" / (name + ".openapi.json")).read_text(encoding="utf-8"))
        if china["servers"][0]["url"] != "https://api.aholo3d.cn":
            raise ValueError("Unexpected China gateway")
        if shape(international) != shape(china):
            raise ValueError("Regional request/response contract differs: " + name)
        print(name + ": " + str(len(china["paths"])) + " paths; normalized request/response/security shapes match")
    resource = json.loads((API / "china/oauth-resource.json").read_text(encoding="utf-8"))
    authorization = json.loads((API / "china/oauth-authorization-server.json").read_text(encoding="utf-8"))
    assert resource["resource"] == "https://api.aholo3d.cn"
    assert resource["authorization_servers"] == ["https://www.aholo3d.cn"]
    assert authorization["issuer"] == "https://www.aholo3d.cn"
    for key, path in {"authorization_endpoint":"authorize", "token_endpoint":"token", "registration_endpoint":"register"}.items():
        assert authorization[key] == "https://www.aholo3d.cn/oauth/" + path
    assert authorization["scopes_supported"] == ["get_user_info"]
    assert "S256" in authorization["code_challenge_methods_supported"]
    assert "none" in authorization["token_endpoint_auth_methods_supported"]
    assert {"authorization_code", "refresh_token"} <= set(authorization["grant_types_supported"])
    asset = json.loads((API / "china/asset.openapi.json").read_text(encoding="utf-8"))
    ous_servers = {server["url"] for path in asset["paths"].values() for operation in path.values() if isinstance(operation, dict) for server in operation.get("servers", [])}
    assert "https://ous-cos.kujiale.com" in ous_servers
    print("China gateway, OUS and public-client PKCE discovery snapshots verified. Runtime region support and live account acceptance are separate gates.")

if __name__ == "__main__":
    check()
