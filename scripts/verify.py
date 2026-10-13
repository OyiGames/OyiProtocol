#!/usr/bin/env python3
"""Generate the C++ and C# bindings for schema/oyi_protocol.fbs, build both round-trip programs and
check that frames written by one language read back in the other. CI runs exactly this script.

Checks, in order:
  1. flatc is the pinned version (OyiEngine's vcpkg port uses the same one).
  2. Tracked files use LF endings; everything but Markdown and third_party/ is ASCII.
  3. The vendored C# runtime (third_party/flatbuffers-net) is the same release as flatc.
  4. The schema's "protocol_version: N" header line matches the tag's MAJOR (tag builds only).
  5. flatc --conform against the newest earlier v* tag (skipped before the first tag).
  6. Bindings generate, both programs build, and the 2x2 read/write matrix passes.
"""

from __future__ import annotations

import argparse
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

FLATC_VERSION = "25.12.19"
ROOT = Path(__file__).resolve().parent.parent
SCHEMA = ROOT / "schema" / "oyi_protocol.fbs"
CSHARP_RUNTIME = ROOT / "third_party" / "flatbuffers-net"
CPP_FLAGS = ["--cpp", "--scoped-enums", "--no-includes"]  # same as OyiEngine's oyi_rpc


def run(cmd: list[str], **kwargs) -> subprocess.CompletedProcess:
    print("+ " + " ".join(str(part) for part in cmd), flush=True)
    return subprocess.run([str(part) for part in cmd], check=True, **kwargs)


def git(*args: str) -> str:
    return subprocess.run(["git", *args], cwd=ROOT, check=True, capture_output=True, text=True).stdout.strip()


def check_flatc(flatc: str) -> None:
    out = subprocess.run([flatc, "--version"], check=True, capture_output=True, text=True).stdout.strip()
    if out != f"flatc version {FLATC_VERSION}":
        sys.exit(f"error: expected flatc {FLATC_VERSION}, got '{out}' from {flatc}")
    print(f"ok: {out}")


def check_sources() -> None:
    """Every tracked file uses LF endings; our own non-Markdown files are ASCII (ADR-0021)."""
    bad: list[str] = []
    for rel in git("ls-files").splitlines():
        data = (ROOT / rel).read_bytes()
        if b"\r\n" in data:
            bad.append(f"{rel}: CRLF line endings")
        own = not rel.endswith(".md") and not rel.startswith("third_party/")
        if own and any(byte > 0x7F for byte in data):
            bad.append(f"{rel}: non-ASCII bytes")
    if bad:
        sys.exit("error: source conventions\n  " + "\n  ".join(bad))
    print("ok: LF endings everywhere, ASCII outside Markdown and third_party/")


def check_vendored_runtime() -> None:
    marker = f"FLATBUFFERS_{FLATC_VERSION.replace('.', '_')}()"
    constants = CSHARP_RUNTIME / "FlatBuffers" / "FlatBufferConstants.cs"
    if marker not in constants.read_text(encoding="utf-8-sig"):
        sys.exit(f"error: {constants.relative_to(ROOT).as_posix()} lacks {marker}; re-vendor it with flatc")
    print(f"ok: vendored C# runtime is {FLATC_VERSION}")


def schema_protocol_version() -> int:
    match = re.search(r"^// protocol_version: (\d+)\b", SCHEMA.read_text(encoding="ascii"), re.MULTILINE)
    if match is None:
        sys.exit("error: schema header lacks a '// protocol_version: N' line")
    return int(match.group(1))


def check_tag(tag: str | None) -> None:
    version = schema_protocol_version()
    if not tag:
        print(f"ok: protocol_version {version} (not a tag build)")
        return
    match = re.fullmatch(r"v(\d+)\.(\d+)\.(\d+)", tag)
    if match is None:
        sys.exit(f"error: tag '{tag}' is not vMAJOR.MINOR.PATCH")
    if int(match.group(1)) != version:
        sys.exit(f"error: tag {tag} has MAJOR {match.group(1)} but the schema says protocol_version {version}")
    print(f"ok: tag {tag} matches protocol_version {version}")


def previous_tag() -> str | None:
    head = git("rev-parse", "HEAD")
    for tag in git("tag", "--list", "v*", "--sort=-v:refname").splitlines():
        if git("rev-list", "-n", "1", tag) != head:
            return tag
    return None


def check_conform(flatc: str, out: Path) -> None:
    tag = previous_tag()
    if tag is None:
        print("skip: flatc --conform (no earlier v* tag)")
        return
    base = out / f"conform-{tag}.fbs"
    base.write_text(git("show", f"{tag}:schema/oyi_protocol.fbs") + "\n", encoding="ascii")
    run([flatc, "--conform", base, SCHEMA])
    print(f"ok: schema conforms to {tag}")


def build_and_roundtrip(flatc: str, cxx: str, flatbuffers_root: Path, out: Path) -> None:
    gen_cpp, gen_cs = out / "generated" / "cpp", out / "generated" / "csharp"
    run([flatc, *CPP_FLAGS, "-o", gen_cpp, SCHEMA])
    run([flatc, "--csharp", "-o", gen_cs, SCHEMA])

    exe = out / ("roundtrip_cpp.exe" if os.name == "nt" else "roundtrip_cpp")
    warnings = ["-Wall", "-Wextra", "-Wpedantic", "-Werror"]
    includes = ["-isystem", flatbuffers_root / "include", "-isystem", gen_cpp]
    run([cxx, "-std=c++20", "-O1", *warnings, *includes, ROOT / "tests" / "cpp" / "roundtrip.cpp", "-o", exe])

    cs_out = out / "csharp"
    project = ROOT / "tests" / "csharp" / "RoundTrip.csproj"
    inputs = [f"-p:GeneratedDir={gen_cs}"]
    run(
        [
            "dotnet",
            "build",
            project,
            "-c",
            "Release",
            "-nologo",
            "--disable-build-servers",
            "-warnaserror",
            "-o",
            cs_out,
            *inputs,
        ]
    )

    cpp, cs = [exe], ["dotnet", cs_out / "roundtrip.dll"]
    from_cpp, from_cs = out / "from_cpp.bin", out / "from_cs.bin"
    run([*cpp, "write", from_cpp])
    run([*cs, "write", from_cs])
    for reader in (cpp, cs):
        for frames in (from_cpp, from_cs):
            run([*reader, "read", frames])
    print("ok: C++ and C# read each other's frames")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--flatc", default=shutil.which("flatc") or "flatc")
    parser.add_argument(
        "--flatbuffers-root",
        type=Path,
        required=True,
        help=f"google/flatbuffers checkout at v{FLATC_VERSION}; only include/ (the C++ headers) is used",
    )
    parser.add_argument("--cxx", default=os.environ.get("CXX", "c++"))
    parser.add_argument("--out", type=Path, default=ROOT / "build" / "verify")
    parser.add_argument("--tag", default=None, help="tag being built; GITHUB_REF is used when it names a tag")
    args = parser.parse_args()

    tag = args.tag
    ref = os.environ.get("GITHUB_REF", "")
    if tag is None and ref.startswith("refs/tags/"):
        tag = ref.removeprefix("refs/tags/")

    if args.out.exists():
        shutil.rmtree(args.out)
    args.out.mkdir(parents=True)

    check_flatc(args.flatc)
    check_sources()
    check_vendored_runtime()
    check_tag(tag)
    check_conform(args.flatc, args.out)
    build_and_roundtrip(args.flatc, args.cxx, args.flatbuffers_root.resolve(), args.out.resolve())


if __name__ == "__main__":
    main()
