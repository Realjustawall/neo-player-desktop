from __future__ import annotations

import hashlib
import json
import os
import shutil
import subprocess
import threading
import urllib.request
import zipfile
from pathlib import Path
from typing import Any, Callable


DEFAULT_RUNTIME_URL = (
    'https://github.com/Realjustawall/neo-player-desktop/'
    'releases/latest/download/NEO-Player-Lyrics-AI.zip'
)


class AIRuntimeManager:
    """Downloads the optional lyrics engine without inflating the main app."""

    def __init__(self, root: str | Path, runtime_url: str | None = None):
        self.root = Path(root)
        self.runtime_url = runtime_url or os.environ.get('NEO_AI_RUNTIME_URL') or DEFAULT_RUNTIME_URL
        self._lock = threading.Lock()
        self._cancel = threading.Event()
        self._thread: threading.Thread | None = None
        self._state: dict[str, Any] = {'phase': 'ready', 'progress': 0, 'message': ''}

    @property
    def executable(self) -> Path:
        name = 'NEOLyricsAI.exe' if os.name == 'nt' else 'NEOLyricsAI'
        return self.root / name

    def status(self) -> dict[str, Any]:
        with self._lock:
            state = dict(self._state)
        state.update({'installed': self.executable.is_file(), 'busy': bool(self._thread and self._thread.is_alive())})
        return state

    def _set(self, **values: Any) -> None:
        with self._lock:
            self._state.update(values)

    def _start(self, target: Callable[[], None]) -> dict[str, Any]:
        if self._thread and self._thread.is_alive():
            raise RuntimeError('Another AI download is already running')
        self._cancel.clear()
        self._thread = threading.Thread(target=target, daemon=True)
        self._thread.start()
        return self.status()

    def install(self) -> dict[str, Any]:
        return self._start(self._install)

    def cancel(self) -> dict[str, Any]:
        self._cancel.set()
        self._set(message='Cancelling…')
        return self.status()

    def remove(self) -> dict[str, Any]:
        if self._thread and self._thread.is_alive():
            raise RuntimeError('Cancel the active download first')
        if self.root.exists():
            shutil.rmtree(self.root)
        self._set(phase='ready', progress=0, message='')
        return self.status()

    def _expected_checksum(self) -> str:
        with urllib.request.urlopen(self.runtime_url + '.sha256', timeout=30) as response:
            value = response.read(4096).decode('ascii', errors='ignore').strip().split()[0].lower()
        if len(value) != 64 or any(c not in '0123456789abcdef' for c in value):
            raise RuntimeError('Invalid AI runtime checksum')
        return value

    def _install(self) -> None:
        archive = self.root.parent / 'neo-ai-runtime.download'
        staging = self.root.parent / 'neo-ai-runtime.staging'
        try:
            self.root.parent.mkdir(parents=True, exist_ok=True)
            self._set(phase='downloading-runtime', progress=0, message='Downloading AI engine')
            expected = self._expected_checksum()
            request = urllib.request.Request(self.runtime_url, headers={'User-Agent': 'NEO-Player'})
            digest = hashlib.sha256()
            with urllib.request.urlopen(request, timeout=60) as response, archive.open('wb') as output:
                total = int(response.headers.get('Content-Length') or 0)
                received = 0
                while True:
                    if self._cancel.is_set():
                        raise InterruptedError('Download cancelled')
                    chunk = response.read(1024 * 512)
                    if not chunk:
                        break
                    output.write(chunk); digest.update(chunk); received += len(chunk)
                    self._set(progress=round(received * 100 / total) if total else 0)
            if digest.hexdigest().lower() != expected:
                raise RuntimeError('AI runtime checksum verification failed')
            self._set(phase='installing-runtime', progress=100, message='Installing AI engine')
            if staging.exists():
                shutil.rmtree(staging)
            staging.mkdir(parents=True)
            with zipfile.ZipFile(archive) as package:
                base = staging.resolve()
                for member in package.infolist():
                    target = (staging / member.filename).resolve()
                    if target != base and base not in target.parents:
                        raise RuntimeError('Unsafe AI runtime archive')
                package.extractall(staging)
            if self.root.exists():
                shutil.rmtree(self.root)
            staging.replace(self.root)
            if not self.executable.is_file():
                raise RuntimeError('AI runtime executable is missing')
            self._set(phase='installed', progress=100, message='AI engine is ready')
        except InterruptedError as exc:
            self._set(phase='cancelled', progress=0, message=str(exc))
        except Exception as exc:
            self._set(phase='error', progress=0, message=str(exc))
        finally:
            archive.unlink(missing_ok=True)
            if staging.exists():
                shutil.rmtree(staging, ignore_errors=True)

    def ensure_model(self, model: str) -> dict[str, Any]:
        if not self.executable.is_file():
            raise RuntimeError('Install the AI engine first')
        allowed = {'tiny', 'base', 'small', 'medium'}
        if model not in allowed:
            raise ValueError('Unsupported model')
        return self._start(lambda: self._run_model_download(model))

    def _run_model_download(self, model: str) -> None:
        try:
            self._set(phase='downloading-model', progress=0, message=f'Downloading {model} model')
            result = self.run({
                'action': 'ensure-model', 'model': model,
                'model_root': str(self.root.parent / 'ai-models'),
            }, timeout=60 * 60)
            self._set(phase='model-ready', progress=100, message=str(result.get('model_path', '')))
        except Exception as exc:
            self._set(phase='error', progress=0, message=str(exc))

    def run(self, payload: dict[str, Any], timeout: int = 60 * 30) -> dict[str, Any]:
        if not self.executable.is_file():
            raise RuntimeError('AI engine is not installed')
        completed = subprocess.run(
            [str(self.executable), json.dumps(payload, ensure_ascii=False)],
            capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=timeout,
            creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0),
        )
        if completed.returncode:
            raise RuntimeError(completed.stderr.strip() or completed.stdout.strip() or 'AI engine failed')
        try:
            return json.loads(completed.stdout)
        except json.JSONDecodeError as exc:
            raise RuntimeError('AI engine returned an invalid response') from exc
