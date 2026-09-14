#!/usr/bin/env python3
"""Local package build for Peek.

Produces the same layout .github/workflows/release.yml produces, and it has to stay that
way: this script exists so a local package is the same artifact CI ships, not a
similar-looking one. Two details are load-bearing rather than stylistic -

  * the two executables publish to SEPARATE folders (Peek.Desktop into dist/win-x64,
    Peek.Worker into dist/win-x64/worker). They are published with different trim settings,
    so sharing one folder means one's copy of a shared runtime file overwrites the other's -
    observed as a trimmed 3MB System.Private.CoreLib.dll clobbering the untrimmed 16MB one,
    which corrupts whichever exe loses the race. WorkerConnection.WorkerExecutablePath
    expects the worker subfolder.

  * the LibVLC plugin tree is pruned to the handful of plugins audio playback actually needs.
    The full set is about 100MB - by far the largest thing in the package.
"""

import argparse
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).parent.resolve()
SRC = ROOT / "src"

DESKTOP_PROJ = SRC / "client" / "Peek.Desktop" / "Peek.Desktop.csproj"
WORKER_PROJ = SRC / "worker" / "Peek.Worker" / "Peek.Worker.csproj"

DIST = ROOT / "dist" / "win-x64"
WORKER_DIST = DIST / "worker"
INSTALLER_SCRIPT = ROOT / "pack" / "peek.iss"

# Mirrors the keep-list in release.yml. Anything not named here is deleted; any category not
# named here is deleted wholesale.
LIBVLC_KEEP = {
    "access": ["libfilesystem_plugin.dll"],
    "demux": ["libwav_plugin.dll"],
    "codec": ["libaraw_plugin.dll", "libadpcm_plugin.dll", "liblpcm_plugin.dll"],
    "audio_output": [
        "libmmdevice_plugin.dll",
        "libwasapi_plugin.dll",
        "libdirectsound_plugin.dll",
        "libwaveout_plugin.dll",
    ],
    "audio_filter": [
        "libaudio_format_plugin.dll",
        "libsimple_channel_mixer_plugin.dll",
        "libtrivial_channel_mixer_plugin.dll",
        "libugly_resampler_plugin.dll",
        "libspeex_resampler_plugin.dll",
    ],
    "audio_mixer": ["libfloat_mixer_plugin.dll", "libinteger_mixer_plugin.dll"],
}


def run(cmd: list[str], cwd: Path | None = None) -> None:
    print(f"  $ {' '.join(cmd)}")
    result = subprocess.run(cmd, cwd=str(cwd) if cwd else None)
    if result.returncode != 0:
        print(f"\n[ERROR] Command failed with exit code {result.returncode}")
        sys.exit(result.returncode)


def publish(project: Path, output: Path, version: str) -> None:
    if not project.exists():
        print(f"[ERROR] Project not found: {project}")
        sys.exit(1)

    run([
        "dotnet", "publish", str(project),
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "true",
        "-p:StripSymbols=true",
        f"-p:Version={version}",
        "-o", str(output),
    ])


def prune_libvlc() -> None:
    libvlc = DIST / "libvlc"
    if not libvlc.exists():
        return

    for d in libvlc.iterdir():
        if d.is_dir() and d.name != "win-x64":
            print(f"  Removing libvlc/{d.name}")
            shutil.rmtree(d)

    plugins = libvlc / "win-x64" / "plugins"
    if not plugins.exists():
        return

    for category in plugins.iterdir():
        if not category.is_dir():
            continue
        keep = LIBVLC_KEEP.get(category.name)
        if keep is None:
            shutil.rmtree(category)
            continue
        for plugin in category.iterdir():
            if plugin.is_file() and plugin.name not in keep:
                plugin.unlink()


def strip_pdbs() -> None:
    pdbs = list(DIST.rglob("*.pdb"))
    for pdb in pdbs:
        pdb.unlink()
    if pdbs:
        print(f"  Removed {len(pdbs)} .pdb file(s)")


def build_installer(version: str) -> None:
    iscc = shutil.which("iscc") or shutil.which("ISCC")
    if iscc is None:
        print("  [SKIP] Inno Setup (iscc.exe) is not on PATH - no installer built.")
        print("         Install it from https://jrsoftware.org/isdl.php to package locally.")
        return

    run([
        iscc, str(INSTALLER_SCRIPT),
        f'/DPeekDistDir={DIST}',
        f'/DPeekVersion={version}',
        "/DPeekBuildMode=self-contained",
    ])


def verify() -> None:
    total = 0
    print(f"\n[Build outputs]  {DIST.relative_to(ROOT)}")
    for p in sorted(DIST.rglob("*")):
        if p.is_file():
            total += p.stat().st_size

    print(f"  {sum(1 for p in DIST.rglob('*') if p.is_file())} files, "
          f"{total / 1_048_576:.1f} MB total")

    # Named explicitly because a package missing either of these is broken in a way that only
    # shows up after installing: no UI, or a UI with no automation, speech, or data.
    for required in (DIST / "Peek.Desktop.exe", WORKER_DIST / "Peek.Worker.exe"):
        status = "OK " if required.exists() else "MISSING"
        print(f"  [{status}] {required.relative_to(DIST)}")
        if not required.exists():
            sys.exit(1)


def main() -> None:
    parser = argparse.ArgumentParser(description="Peek - local package build")
    parser.add_argument("--version", default="0.1.0", help="Version stamped into the binaries and installer")
    parser.add_argument("--no-installer", action="store_true", help="Publish only; don't run Inno Setup")
    parser.add_argument("--clean-only", action="store_true")
    args = parser.parse_args()

    print("=" * 60)
    print(" Peek - local package build")
    print(f" Version: {args.version}")
    print("=" * 60)

    print("\n[0] Preparing output directory ...")
    if DIST.exists():
        print(f"  Removing {DIST}")
        shutil.rmtree(DIST)
    DIST.mkdir(parents=True, exist_ok=True)

    if args.clean_only:
        print("Clean done.")
        return

    print("\n[1/4] Publishing Peek.Desktop (UI) ...")
    publish(DESKTOP_PROJ, DIST, args.version)

    print("\n[2/4] Pruning LibVLC plugins ...")
    prune_libvlc()

    print("\n[3/4] Publishing Peek.Worker (into worker/) ...")
    publish(WORKER_PROJ, WORKER_DIST, args.version)

    strip_pdbs()
    verify()

    if not args.no_installer:
        print("\n[4/4] Building the installer ...")
        build_installer(args.version)

    print("\nDone!")
    print("=" * 60)


if __name__ == "__main__":
    main()
