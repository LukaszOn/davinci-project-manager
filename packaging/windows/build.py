"""Build a per-user installer from an existing portable Windows build."""

import argparse
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import tempfile
from xml.etree.ElementTree import parse
from zipfile import ZipFile


def stage_payload(archive_path, root_name, destination, uninstall_list):
    files = set()
    folders = set()
    size = 0
    with ZipFile(archive_path) as archive:
        for entry in archive.infolist():
            if entry.is_dir():
                continue
            bundle = PurePosixPath(entry.filename)
            if len(bundle.parts) < 2 or bundle.parts[0] != root_name:
                raise ValueError(f"Unexpected bundle path: {entry.filename}")
            parts = bundle.parts[1:]
            if any(not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._ -]*", part) or part.endswith((" ", ".")) for part in parts):
                raise ValueError(f"Unexpected file name: {entry.filename}")
            target = destination.joinpath(*parts)
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(entry) as source, target.open("xb") as output:
                shutil.copyfileobj(source, output)
            files.add("\\".join(parts))
            folders.update("\\".join(parts[:depth]) for depth in range(1, len(parts)))
            size += entry.file_size

    required = {"DaVinciProjectManager.exe", "DaVinciProjectManager.dll", "LICENSE", "sql\\control.sql", "sql\\register.sql"}
    if not required <= files or "Uninstall.exe" in files:
        raise ValueError("The portable archive lacks the application payload or contains an uninstaller.")
    commands = [f'Delete "$INSTDIR\\{path}"' for path in sorted(files)]
    commands.extend(f'RMDir "$INSTDIR\\{folder}"' for folder in sorted(folders, key=lambda name: (-name.count("\\"), name)))
    uninstall_list.write_text("\n".join(commands) + "\n", encoding="utf-8-sig")
    return (size + 1023) // 1024


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("portable_zip", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--makensis", required=True, type=Path)
    parser.add_argument("--test", action="store_true", help="Use an isolated Start menu entry and uninstall registration")
    args = parser.parse_args()

    script = Path(__file__).resolve().parent
    project = script.parent.parent
    version = parse(project / "ProjectLibraryDesk.csproj").findtext("./PropertyGroup/Version")
    match = re.fullmatch(r"(\d+\.\d+\.\d+)(?:-[A-Za-z]+\.(\d+))?", version or "")
    if match is None:
        raise ValueError(f"Unsupported version: {version}")
    version_number = match[1] + "." + (match[2] or "0")
    output = args.output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)

    with tempfile.TemporaryDirectory(prefix="dpm-setup-") as temp:
        staging = Path(temp)
        payload = staging / "payload"
        uninstall_list = staging / "remove-payload.nsh"
        root_name = f"DaVinci-Project-Manager-{version}"
        size = stage_payload(args.portable_zip, root_name, payload, uninstall_list)
        template = staging / "DaVinciProjectManager.nsi"
        template.write_text((script / template.name).read_text(encoding="utf-8"), encoding="utf-8-sig")
        defines = {
            "PAYLOAD": payload,
            "REMOVE_PAYLOAD": uninstall_list,
            "OUTPUT": output,
            "APP_ICON": project / "Assets" / "Desk.ico",
            "VERSION": version,
            "FILE_VERSION": version_number,
            "ESTIMATED_KIB": size,
        }
        if args.test:
            defines.update(APP_ID="DaVinciProjectManager.InstallerSmoke", APP_LABEL="DaVinci Project Manager Installer Test")
        subprocess.run([str(args.makensis.resolve()), "/V2", "/WX", *(f"/D{key}={value}" for key, value in defines.items()), str(template)], check=True)


if __name__ == "__main__":
    main()
