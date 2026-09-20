from __future__ import annotations

import json
import sys
from pathlib import Path

from faster_whisper.utils import download_model

from offline_lyrics import OfflineLyricsEngine


def main() -> int:
    try:
        payload = json.loads(sys.argv[1])
        action = str(payload.get('action', ''))
        model_root = Path(payload.get('model_root') or Path.home() / '.neo-player-models').resolve()
        model_root.mkdir(parents=True, exist_ok=True)
        if action == 'ensure-model':
            model_path = download_model(str(payload['model']), output_dir=str(model_root / str(payload['model'])))
            print(json.dumps({'model_path': str(Path(model_path).resolve())}, ensure_ascii=False))
            return 0
        if action == 'transcribe':
            result = OfflineLyricsEngine().transcribe(
                payload['audio_path'], payload['model_path'], payload.get('language', 'auto')
            ).as_dict()
            print(json.dumps(result, ensure_ascii=False))
            return 0
        raise ValueError('Unknown AI action')
    except Exception as exc:
        print(str(exc), file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
