"""
Builds the installer: app -> folder -> zip -> embedded in a native setup executable.

    python tools/build_release.py

The app is published self-contained but NOT as a single file, so nothing is unpacked into the temp
folder at run time; the price is a folder of files, which is what the installer is for. The setup
and uninstall programs are compiled ahead of time, so they need no runtime of their own.

Native AOT needs the MSVC linker. If the build stops at 'vswhere.exe' not being found, put the
Visual Studio installer directory on PATH first:

    C:\Program Files (x86)\Microsoft Visual Studio\Installer
"""

import os
import pathlib
import shutil
import subprocess
import sys
import zipfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
STAGE = ROOT / "build" / "app"
PAYLOAD = ROOT / "installer" / "Payload" / "app.zip"
DIST = ROOT / "dist"
RUNTIME = "win-x64"


def run(*args):
    print("+", " ".join(str(a) for a in args))
    result = subprocess.run(args, cwd=ROOT)
    if result.returncode != 0:
        sys.exit(result.returncode)


def publish_app():
    if STAGE.exists():
        shutil.rmtree(STAGE)
    run("dotnet", "publish", "src/OtpManager.csproj", "-c", "Release", "-r", RUNTIME,
        "--self-contained", "true", "-p:PublishSingleFile=false", "-o", str(STAGE), "--nologo")


def publish_uninstaller():
    run("dotnet", "publish", "uninstaller/Uninstaller.csproj", "-c", "Release", "-r", RUNTIME,
        "-o", str(STAGE), "--nologo")


def make_payload():
    PAYLOAD.parent.mkdir(parents=True, exist_ok=True)
    if PAYLOAD.exists():
        PAYLOAD.unlink()

    # Debug symbols are of no use on the machine this gets installed on.
    files = [p for p in STAGE.rglob("*") if p.is_file() and p.suffix.lower() != ".pdb"]
    with zipfile.ZipFile(PAYLOAD, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in files:
            archive.write(path, path.relative_to(STAGE).as_posix())

    print(f"payload: {len(files)} files, {PAYLOAD.stat().st_size / 1_000_000:.1f} MB")


def publish_installer():
    if DIST.exists():
        shutil.rmtree(DIST)
    run("dotnet", "publish", "installer/Installer.csproj", "-c", "Release", "-r", RUNTIME,
        "-o", str(DIST), "--nologo")

    for junk in DIST.glob("*.pdb"):
        junk.unlink()


def main():
    publish_app()
    publish_uninstaller()
    make_payload()
    publish_installer()

    setup = DIST / "OtpManagerSetup.exe"
    print(f"\nsetup: {setup} ({setup.stat().st_size / 1_000_000:.1f} MB)")


if __name__ == "__main__":
    main()
