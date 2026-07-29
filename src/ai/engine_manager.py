from __future__ import annotations

from typing import Dict, Optional

from engines import RealEsrganEngine
from engines.base_engine import BaseEngine


class EngineManager:
    def __init__(self) -> None:
        self._engines: Dict[str, BaseEngine] = {
            RealEsrganEngine.NAME: RealEsrganEngine(),
        }

    def get(self, engine_name: str) -> Optional[BaseEngine]:
        return self._engines.get(engine_name)

    def list(self) -> Dict[str, BaseEngine]:
        return dict(self._engines)
