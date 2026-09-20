from __future__ import annotations

import json
import sys
import wave
from pathlib import Path

from faster_whisper.utils import download_model
import av
from av.audio.resampler import AudioResampler

from offline_lyrics import OfflineLyricsEngine


def main() -> int:
    try:
        payload = json.loads(sys.argv[1])
        action = str(payload.get('action', ''))
        model_root = Path(payload.get('model_root') or Path.home() / '.neo-player-models').resolve()
        model_root.mkdir(parents=True, exist_ok=True)
        if action == 'self-test':
            print(json.dumps({'ok': True, 'av': av.__version__}))
            return 0
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
        if action == 'extract-audio':
            source = Path(payload['video_path']).resolve()
            output = Path(payload['output_path']).resolve()
            if not source.is_file():
                raise ValueError('Video file was not found')
            output.parent.mkdir(parents=True, exist_ok=True)
            frames = 0
            with av.open(str(source)) as container, wave.open(str(output), 'wb') as wav:
                stream = next((item for item in container.streams if item.type == 'audio'), None)
                if stream is None:
                    raise ValueError('The video has no audio track')
                wav.setnchannels(2); wav.setsampwidth(2); wav.setframerate(44100)
                resampler = AudioResampler(format='s16', layout='stereo', rate=44100)
                for frame in container.decode(stream):
                    for converted in resampler.resample(frame):
                        wav.writeframes(converted.to_ndarray().tobytes()); frames += converted.samples
                for converted in resampler.resample(None):
                    wav.writeframes(converted.to_ndarray().tobytes()); frames += converted.samples
            print(json.dumps({'path': str(output), 'duration': frames / 44100}, ensure_ascii=False))
            return 0
        raise ValueError('Unknown AI action')
    except Exception as exc:
        print(str(exc), file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
