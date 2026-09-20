from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable


def lrc_timestamp(seconds: float) -> str:
    value = max(0.0, float(seconds or 0))
    minutes = int(value // 60)
    remainder = value - minutes * 60
    return f'[{minutes:02d}:{remainder:05.2f}]'


def segments_to_lrc(segments: list[dict[str, Any]]) -> str:
    lines: list[str] = []
    for segment in segments:
        text = str(segment.get('text', '')).strip()
        if text:
            lines.append(f"{lrc_timestamp(float(segment.get('start', 0)))}{text}")
    return '\n'.join(lines)


@dataclass
class TranscriptionResult:
    language: str
    probability: float
    duration: float
    plain: str
    lrc: str
    segments: list[dict[str, Any]]
    words: list[dict[str, Any]]

    def as_dict(self) -> dict[str, Any]:
        return {
            'language': self.language, 'probability': self.probability, 'duration': self.duration,
            'plain': self.plain, 'lrc': self.lrc, 'segments': self.segments, 'words': self.words,
        }


class OfflineLyricsEngine:
    """Local-only Whisper transcription. No model or audio network access is allowed."""

    def __init__(self, model_factory: Callable[..., Any] | None = None):
        self._model_factory = model_factory

    def _factory(self):
        if self._model_factory:
            return self._model_factory
        try:
            from faster_whisper import WhisperModel
        except ImportError as exc:
            raise RuntimeError('Offline AI component is not installed. Install requirements-ai.txt.') from exc
        return WhisperModel

    def transcribe(self, audio_path: str | Path, model_path: str | Path, language: str = 'auto') -> TranscriptionResult:
        audio = Path(audio_path).resolve()
        model_dir = Path(model_path).resolve()
        if not audio.is_file():
            raise ValueError('Audio file does not exist')
        if not model_dir.is_dir():
            raise ValueError('Whisper model folder does not exist')
        # Passing a resolved local directory prevents implicit model downloads.
        model = self._factory()(str(model_dir), device='auto', compute_type='int8', local_files_only=True)
        segments_iter, info = model.transcribe(
            str(audio), language=None if language in {'', 'auto'} else language,
            beam_size=5, vad_filter=True, word_timestamps=True, condition_on_previous_text=True,
        )
        segments: list[dict[str, Any]] = []
        words: list[dict[str, Any]] = []
        for item in segments_iter:
            row = {'start': float(item.start), 'end': float(item.end), 'text': str(item.text).strip()}
            segments.append(row)
            for word in getattr(item, 'words', None) or []:
                words.append({'start': float(word.start or 0), 'end': float(word.end or 0), 'text': str(word.word).strip()})
        plain = '\n'.join(row['text'] for row in segments if row['text'])
        return TranscriptionResult(
            language=str(getattr(info, 'language', language if language != 'auto' else '')),
            probability=float(getattr(info, 'language_probability', 0) or 0),
            duration=float(getattr(info, 'duration', 0) or 0),
            plain=plain, lrc=segments_to_lrc(segments), segments=segments, words=words,
        )
