from __future__ import annotations

from abc import ABC, abstractmethod
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any, Dict


@dataclass
class EngineResult:
    success: bool
    engine: str
    input_path: str
    output_path: str
    duration: float
    message: str


class BaseEngine(ABC):
    NAME: str

    def __init__(self, name: str) -> None:
        self.name = name

    @abstractmethod
    def enhance(self, input_path: Path, output_path: Path, options: Dict[str, Any]) -> EngineResult:
        """Enhance an input asset and produce an EngineResult."""
        raise NotImplementedError
