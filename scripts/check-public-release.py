#!/usr/bin/env python3
"""Check the publishable source tree without printing matched private values."""
import argparse
import os
import re
import subprocess
from pathlib import Path
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parent.parent
REQUIRED = ("readme.md", "readme.zh-CN.md", "LICENSE", "NOTICE", "CONTRIBUTING.md",
            "SECURITY.md", "packaging/manifest.yml", "docs/index.md")
PRIVATE_DOCS = {
    "docs/development-plan.md", "docs/implementation-progress.md", "docs/validation.md",
    "docs/host-acceptance.md", "docs/integrated-acceptance.md", "docs/gs-host-acceptance.md",
    "docs/gs-freeze-repair.md", "docs/gs-visibility-stability.md",
}
SECRET = re.compile(rb"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|"
                    rb"gh[pousr]_[A-Za-z0-9]{24,}|github_pat_[A-Za-z0-9_]{30,}|sk-[A-Za-z0-9]{24,}")
LOCAL_PATH = re.compile(r"(?:/Users|/home)/[A-Za-z0-9_.-]+")
PROCESS_NOTE = re.compile(r"remote/(?:gpt|gemini|kimi|glm|claude)|completion_tokens|"
                          r"prompt_tokens|delegate_task|rollout_summaries", re.I)
LINK = re.compile(r"!?\[[^\]]*\]\(([^)]+)\)")

def git(root, *args):
    env = {key: value for key, value in os.environ.items() if not key.startswith("GIT_")}
    env.update(GIT_CONFIG_GLOBAL=os.devnull, GIT_CONFIG_NOSYSTEM="1", GIT_OPTIONAL_LOCKS="0")
    return subprocess.check_output(["git", "-C", str(root), *args], env=env)

def forbidden_path(name):
    parts = Path(name).parts
    return (name in PRIVATE_DOCS or any(p in {"artifacts", ".agents", ".codex", ".mcneel", ".venv", "__pycache__", "bin", "obj"} for p in parts)
            or Path(name).name in {"AGENTS.md", "AGENTS.override.md"}
            or Path(name).name.startswith(".env") or name.endswith((".bundle", ".log", ".pyc")))

def file_errors(root, name, data):
    errors = []
    if forbidden_path(name):
        errors.append(f"{name}: private/generated file")
    if SECRET.search(data):
        errors.append(f"{name}: possible credential material (value omitted)")
    try:
        text = data.decode("utf-8")
    except UnicodeDecodeError:
        return errors
    if name.endswith(".md"):
        if LOCAL_PATH.search(text):
            errors.append(f"{name}: machine-specific user path")
        if PROCESS_NOTE.search(text):
            errors.append(f"{name}: internal execution narrative")
        for match in LINK.finditer(text):
            target = match[1].strip().strip("<>")
            if urlsplit(target).scheme or target.startswith("#"):
                continue
            relative = unquote(urlsplit(target).path)
            if not relative:
                continue
            path = (root / name).parent / relative
            try:
                resolved = path.resolve()
                resolved.relative_to(root.resolve())
            except ValueError:
                errors.append(f"{name}: link leaves source tree")
                continue
            if not resolved.exists():
                errors.append(f"{name}: broken local link to {relative}")
    return errors

def check(root, history=False):
    errors = [f"{name}: required public file missing" for name in REQUIRED if not (root / name).is_file()]
    names = set(git(root, "ls-files", "--cached", "--others", "--exclude-standard", "-z").decode().split("\0")) - {""}
    for name in sorted(names):
        path = root / name
        if path.is_symlink():
            errors.append(f"{name}: source symlink requires explicit review")
        elif path.is_file():
            errors.extend(file_errors(root, name, path.read_bytes()))
    if history:
        identities = git(root, "log", "--all", "--format=%an%x00%cn%x00%ae%x00%ce").decode().splitlines()
        bad = 0
        for row in identities:
            author, committer, author_email, committer_email = row.split("\0")
            if (any(n.casefold() not in {"qrost", "curiousarc"} for n in (author, committer))
                    or any(e.casefold() != "qrost@users.noreply.github.com" for e in (author_email, committer_email))):
                bad += 1
        if bad:
            errors.append(f"history: {bad} commits have non-public author/committer identities")
        old_paths = {row.partition(" ")[2] for row in git(root, "rev-list", "--objects", "--all").decode().splitlines()}
        if any(forbidden_path(name) for name in old_paths if name):
            errors.append("history: private or retired process documents remain reachable")
    return errors

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--check-history", action="store_true")
    args = parser.parse_args()
    errors = check(args.root.resolve(), args.check_history)
    for error in errors:
        print(error)
    if errors:
        return 1
    print("Public source checks passed. Pattern scanning is not a guarantee that all sensitive content is absent.")
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
