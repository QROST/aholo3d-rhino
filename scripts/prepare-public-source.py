#!/usr/bin/env python3
"""Create a separate public-source candidate with a new, alias-only Git history. Never push."""
import argparse
import hashlib
import importlib.util
import io
import json
import os
import re
from pathlib import Path, PurePosixPath
import subprocess
import tarfile
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location("public_check", ROOT / "scripts/check-public-release.py")
public_check = importlib.util.module_from_spec(spec)
spec.loader.exec_module(public_check)

def git(root, *args):
    # Keep inherited Git identity, filters and hooks out of the new public history.
    env = {key: value for key, value in os.environ.items() if not key.startswith("GIT_")}
    env.update(GIT_CONFIG_GLOBAL=os.devnull, GIT_CONFIG_NOSYSTEM="1", GIT_OPTIONAL_LOCKS="0")
    return subprocess.check_output(["git", "-C", str(root), *args], env=env)

def safe_member(member):
    path = PurePosixPath(member.name)
    if (path.is_absolute() or ".." in path.parts or "\\" in member.name or "\0" in member.name
            or re.match(r"^[A-Za-z]:", member.name) or ".git" in path.parts
            or member.issym() or member.islnk()):
        raise ValueError("Unsafe source archive entry.")
    if not member.isfile() and not member.isdir():
        raise ValueError("Unsupported source archive entry.")
    return path

def prepare(root, output):
    root = root.resolve()
    output = output.resolve()
    git_directory = Path(git(root, "rev-parse", "--path-format=absolute", "--git-common-dir").decode().strip()).resolve()
    if output == git_directory or git_directory in output.parents:
        raise ValueError("Output cannot be inside Git metadata.")
    if output == root or root in output.parents:
        try:
            git(root, "check-ignore", "--no-index", str(output))
        except subprocess.CalledProcessError:
            raise ValueError("Output inside the source tree must be explicitly ignored.") from None
    if git(root, "status", "--porcelain").strip():
        raise ValueError("Commit the reviewed source tree before creating a public candidate.")
    errors = public_check.check(root)
    if errors:
        raise ValueError("Public source checks failed: " + "; ".join(errors))
    original = git(root, "rev-parse", "HEAD").decode().strip()
    output.mkdir(parents=True, exist_ok=True)
    stage = Path(tempfile.mkdtemp(prefix="public-source.", dir=output))
    source = stage / "aholo3d-rhino"
    source.mkdir()
    payload = {}
    with tarfile.open(fileobj=io.BytesIO(git(root, "archive", "--format=tar", original)), mode="r:") as archive:
        for member in archive:
            path = safe_member(member)
            if member.isdir():
                continue
            if public_check.forbidden_path(path.as_posix()):
                raise ValueError("Private file in source export.")
            content = archive.extractfile(member).read()
            target = source / path
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(content)
            target.chmod(0o755 if member.mode & 0o111 else 0o644)
            payload[path.as_posix()] = hashlib.sha256(content).hexdigest()
    original_names = set(git(root, "ls-tree", "-rz", "--name-only", original).decode().split("\0")) - {""}
    if original_names != set(payload):
        raise ValueError("Source archive omitted tracked files; review Git export attributes or submodules.")
    for name, digest in payload.items():
        if hashlib.sha256(git(root, "show", original + ":" + name)).hexdigest() != digest:
            raise ValueError("Source archive transformed tracked content; review Git export attributes.")
    git(source, "init", "--quiet", "-b", "main")
    for key, value in (("user.name", "qrost"), ("user.email", "QROST@users.noreply.github.com"),
                       ("commit.gpgsign", "false"), ("core.autocrlf", "false")):
        git(source, "config", key, value)
    git(source, "add", "--force", "--all")
    git(source, "commit", "--quiet", "-m", "Initial public source for Aholo3D for Rhino")
    committed = set(git(source, "ls-tree", "-rz", "--name-only", "HEAD").decode().split("\0")) - {""}
    if committed != set(payload):
        raise ValueError("Public Git tree differs from exported source files.")
    for name, digest in payload.items():
        if hashlib.sha256(git(source, "show", "HEAD:" + name)).hexdigest() != digest:
            raise ValueError("Public Git blob differs from exported source bytes.")
    errors = public_check.check(source, history=True)
    if errors or git(source, "rev-list", "--count", "--all").strip() != b"1":
        raise ValueError("Clean public history verification failed: " + "; ".join(errors))
    if git(source, "remote").strip():
        raise ValueError("Unexpected remote on public candidate.")
    archive_path = stage / "aholo3d-rhino-source.zip"
    with zipfile.ZipFile(archive_path, "w", zipfile.ZIP_DEFLATED) as archive:
        for name in sorted(payload):
            archive.write(source / name, "aholo3d-rhino/" + name)
    with zipfile.ZipFile(archive_path) as archive:
        expected = {"aholo3d-rhino/" + name for name in payload}
        if set(archive.namelist()) != expected:
            raise ValueError("Source archive entries differ.")
        for name, digest in payload.items():
            if hashlib.sha256(archive.read("aholo3d-rhino/" + name)).hexdigest() != digest:
                raise ValueError("Source archive bytes differ.")
    receipt = {
        "developmentSourceCommit": original,
        "publicSourceCommit": git(source, "rev-parse", "HEAD").decode().strip(),
        "publicHistoryCommits": 1, "author": "qrost",
        "sourceDirectory": str(source), "archive": str(archive_path), "files": len(payload),
        "sha256": hashlib.sha256(archive_path.read_bytes()).hexdigest(),
        "pushed": False, "visibilityChanged": False,
    }
    (stage / "private-receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")
    (output / "latest-public-source.json").write_text(json.dumps(receipt, indent=2) + "\n")
    return receipt

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts")
    args = parser.parse_args()
    print(json.dumps(prepare(args.root, args.output.resolve()), indent=2))

if __name__ == "__main__":
    main()
