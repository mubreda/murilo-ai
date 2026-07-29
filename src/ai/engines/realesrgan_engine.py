from __future__ import annotations

import subprocess
import sys
from pathlib import Path
from time import perf_counter
from typing import Any, Dict

from engines.base_engine import BaseEngine, EngineResult


class RealEsrganEngine(BaseEngine):
    NAME = "realesrgan"

    def __init__(self) -> None:
        super().__init__(name=self.NAME)

    def enhance(self, input_path: Path, output_path: Path, options: Dict[str, Any]) -> EngineResult:
        engine_dir = Path(__file__).resolve().parent
        project_dir = engine_dir.parent
        script_path = project_dir / "realesrgan" / "Real-ESRGAN" / "inference_realesrgan.py"
        tile = options.get("tile", 256)
        command = [
            sys.executable,
            str(script_path),
            "-n",
            "RealESRGAN_x4plus",
            "-i",
            str(input_path),
            "-o",
            str(output_path),
            "--tile",
            str(tile),
        ]

        start = perf_counter()
        completed = subprocess.run(
            command,
            capture_output=True,
            text=True,
        )
        duration = perf_counter() - start

        if completed.returncode != 0:
            return EngineResult(
                success=False,
                engine=self.name,
                input_path=str(input_path),
                output_path=str(output_path),
                duration=duration,
                message=completed.stderr.strip() or f"RealEsrganEngine failed with return code {completed.returncode}",
            )

        return EngineResult(
            success=True,
            engine=self.name,
            input_path=str(input_path),
            output_path=str(output_path),
            duration=duration,
            message=completed.stdout.strip() or "RealEsrganEngine completed successfully.",
        )
