#!/usr/bin/env python3
import os
import sys
import pathlib
import importlib.util
from typing import Optional

SEARCH_FILENAME = "degradations.py"
OLD_LINE = "from torchvision.transforms.functional_tensor import rgb_to_grayscale"
NEW_LINE = "from torchvision.transforms.functional import rgb_to_grayscale"


def find_basicsr_package() -> Optional[pathlib.Path]:
    # Try importlib metadata if available without importing the package
    spec = importlib.util.find_spec("basicsr")
    if spec and spec.origin:
        pkg_path = pathlib.Path(spec.origin).resolve().parent
        if pkg_path.exists():
            return pkg_path

    # Search in sys.path entries for an installed basicsr package
    for entry in sys.path:
        if not entry:
            continue
        root = pathlib.Path(entry)
        if not root.exists() or not root.is_dir():
            continue
        candidate = root / "basicsr"
        if candidate.exists() and candidate.is_dir():
            return candidate.resolve()

    # Fallback: search common venv site-packages locations
    base_paths = {pathlib.Path(sys.prefix), pathlib.Path(sys.exec_prefix), pathlib.Path(sys.base_prefix)}
    if hasattr(sys, "real_prefix"):
        base_paths.add(pathlib.Path(sys.real_prefix))

    candidates = set()
    for base in base_paths:
        if not base.exists():
            continue
        for candidate in [
            base / "Lib" / "site-packages",
            base / "lib" / f"python{sys.version_info.major}.{sys.version_info.minor}" / "site-packages",
            base / "site-packages",
        ]:
            if candidate.exists():
                candidates.add(candidate)

    for candidate in candidates:
        package_dir = candidate / "basicsr"
        if package_dir.exists() and package_dir.is_dir():
            return package_dir.resolve()

    return None


def find_degradations_file(package_path: pathlib.Path) -> Optional[pathlib.Path]:
    candidate_paths = [
        package_path / SEARCH_FILENAME,
        package_path / "data" / SEARCH_FILENAME,
    ]
    for candidate in candidate_paths:
        if candidate.exists():
            return candidate

    for candidate in package_path.rglob(SEARCH_FILENAME):
        return candidate

    return None


def patch_file(file_path: pathlib.Path) -> bool:
    text = file_path.read_text(encoding="utf-8")
    if OLD_LINE not in text:
        return False
    updated = text.replace(OLD_LINE, NEW_LINE)
    file_path.write_text(updated, encoding="utf-8")
    return True


def main() -> int:
    package_path = find_basicsr_package()
    if package_path is None:
        print("Erro: pacote 'basicsr' não encontrado na venv atual.")
        return 1

    target_file = find_degradations_file(package_path)
    if target_file is None:
        print(f"Erro: arquivo não encontrado no pacote basicsr: {package_path}")
        return 1

    print(f"Arquivo encontrado: {target_file}")
    if patch_file(target_file):
        print("Patch aplicado")
    else:
        print("Já atualizado")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
