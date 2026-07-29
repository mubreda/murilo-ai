from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict

from engine_manager import EngineManager
from engines.base_engine import EngineResult


def log(message: str) -> None:
    print(message, file=sys.stderr, flush=True)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run a registered AI engine.")
    parser.add_argument("engine", help="Engine name to execute.")
    parser.add_argument("input", help="Path to the input file.")
    parser.add_argument("output", help="Path to the output file or directory.")
    parser.add_argument("--options", default="{}", help="JSON string with engine options.")
    return parser.parse_args()


def parse_options(options_text: str) -> Dict[str, Any]:
    try:
        return json.loads(options_text)
    except json.JSONDecodeError:
        raise ValueError("Options must be valid JSON.")


def main() -> int:
    log("[run_engine] inicio do programa")
    args = parse_args()
    log(
        f"[run_engine] argumentos recebidos: engine={args.engine}, input={args.input}, output={args.output}, options={args.options}",
    )
    options = parse_options(args.options)
    manager = EngineManager()
    engine = manager.get(args.engine)

    if engine is None:
        log(f"[run_engine] engine selecionada nao encontrada: {args.engine}")
        log(f"Engine '{args.engine}' not found.")
        log("[run_engine] fim do programa")
        return 1

    log(f"[run_engine] engine selecionada: {engine.name}")
    log("[run_engine] inicio da execucao da engine")
    result = engine.enhance(Path(args.input), Path(args.output), options)
    log(f"[run_engine] retorno da engine: {result}")
    if isinstance(result, EngineResult):
        print(json.dumps(result.__dict__))
    else:
        log("Engine did not return an EngineResult.")
        log("[run_engine] fim do programa")
        return 1

    log("[run_engine] fim do programa")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
