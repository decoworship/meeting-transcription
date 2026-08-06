"""Gradio web interface for Meeting Transcription."""

import json
import logging
import os
import queue
import re
import subprocess
import tempfile
import threading
import time
import urllib.parse
import warnings
from datetime import datetime
from html import escape
from pathlib import Path
from typing import Optional

import gradio as gr

# Suppress noisy warnings from pyannote (TF32 disabled, std dof) — informational only
warnings.filterwarnings("ignore", message=".*TensorFloat-32.*")
warnings.filterwarnings("ignore", message=".*degrees of freedom.*")

from ..audio.extractor import (
    AudioExtractor,
    SUPPORTED_FORMATS,
    is_audio_only,
)
from ..transcription.base import TranscriptionResult, MODEL_SIZES
from ..transcription.whisper_transcriber import WhisperTranscriber
from ..transcription.faster_whisper_transcriber import FasterWhisperTranscriber
from ..transcription.whisperx_transcriber import WhisperXTranscriber
from ..diarization.speaker_diarizer import SpeakerDiarizer
from ..utils.gpu_detector import is_cuda_available, get_device_info, enable_gpu_optimizations
from . import history, projects, recordings, voices
from .exporters import to_srt, to_vtt, write_docx

logger = logging.getLogger(__name__)

CONFIG_PATH = Path.home() / ".meeting-transcription" / "config.json"
SUPPORTED_EXTENSIONS = [f".{ext.lstrip('.')}" for ext in SUPPORTED_FORMATS]
PIPELINE_STEPS = ["Audio", "Model", "Transcription", "Diarization", "Output"]

# Speaker color palette (used in HTML preview and DOCX)
SPEAKER_HEX_COLORS = [
    "#1f6aa5", "#d9534f", "#2ea84f", "#c47f17",
    "#7b47a1", "#0e8b8b", "#b84a7e", "#0f766e",
]

ASSETS_DIR = Path(__file__).resolve().parent.parent.parent / "assets"
LOGO_SVG_PATH = ASSETS_DIR / "logo.svg"
LOGO_PNG_PATH = ASSETS_DIR / "logo.png"


def render_audio_html(audio_path: Optional[str]) -> str:
    """Render a plain HTML5 audio player pointing at a Gradio-served file URL.

    We bypass gr.Audio because Gradio 6's WaveSurfer-based player loads audio via
    Web Audio API (no src on the <audio> element), which makes programmatic seek
    impossible. With a plain <audio>, audio.currentTime works directly.
    """
    if not audio_path or not os.path.exists(audio_path):
        return (
            '<div style="color:#94a3b8;text-align:center;padding:20px;'
            'border:1px dashed #cbd5e1;border-radius:8px;">'
            'No audio loaded yet — run a transcription first.'
            '</div>'
        )
    encoded = urllib.parse.quote(audio_path, safe="")
    return (
        '<audio id="mt-audio-real" controls preload="auto" '
        'style="width: 100%;" '
        f'src="/gradio_api/file={encoded}">'
        'Your browser does not support audio playback.'
        '</audio>'
    )


def get_logo_path() -> Optional[str]:
    """Return path to logo file (PNG preferred, fallback to SVG)."""
    if LOGO_PNG_PATH.exists():
        return str(LOGO_PNG_PATH)
    if LOGO_SVG_PATH.exists():
        return str(LOGO_SVG_PATH)
    return None


def get_logo_inline_svg() -> str:
    """Return logo SVG content for inline embedding."""
    if LOGO_SVG_PATH.exists():
        try:
            return LOGO_SVG_PATH.read_text(encoding="utf-8")
        except Exception:
            pass
    return ""


# ── Config persistence ──────────────────────────────────────────────


def load_config() -> dict:
    try:
        if CONFIG_PATH.exists():
            with open(CONFIG_PATH, "r", encoding="utf-8") as f:
                return json.load(f)
    except Exception as e:
        logger.warning(f"Failed to load config: {e}")
    return {}


def save_config(
    model_size: str,
    engine: str,
    language: str,
    diarization: bool,
    hf_token: str,
    condition_prev: bool,
    diar_model: str,
    client: str,
    project: str,
    initial_prompt: str = "",
    recognize_voices: bool = True,
    voice_threshold: float = 0.65,
    user_label: str = "You",
):
    config = {
        "model_size": model_size,
        "engine": engine,
        "language": language,
        "diarization": diarization,
        "condition_on_previous_text": condition_prev,
        "diar_model": diar_model,
        "client": client,
        "project": project,
        "initial_prompt": initial_prompt,
        "recognize_voices": recognize_voices,
        "voice_threshold": float(voice_threshold),
        "user_label": user_label,
    }
    if hf_token:
        config["hf_token"] = hf_token
    try:
        CONFIG_PATH.parent.mkdir(parents=True, exist_ok=True)
        with open(CONFIG_PATH, "w", encoding="utf-8") as f:
            json.dump(config, f, indent=2)
    except Exception as e:
        logger.warning(f"Failed to save config: {e}")


# ── Helpers ──────────────────────────────────────────────────────────


def format_time(seconds: float) -> str:
    hours = int(seconds // 3600)
    minutes = int((seconds % 3600) // 60)
    secs = int(seconds % 60)
    if hours > 0:
        return f"{hours:02d}:{minutes:02d}:{secs:02d}"
    return f"{minutes:02d}:{secs:02d}"


def _segments_by_speaker(result: TranscriptionResult) -> dict[str, list]:
    """Group segments by speaker label (excluding 'Unknown')."""
    by_speaker: dict[str, list] = {}
    if not result:
        return by_speaker
    for seg in result.segments:
        if seg.speaker and seg.speaker != "Unknown":
            by_speaker.setdefault(seg.speaker, []).append(seg)
    return by_speaker


def speaker_color_map(result: TranscriptionResult) -> dict[str, str]:
    """Assign a stable color to each speaker."""
    if not result:
        return {}
    speakers = sorted(set(seg.speaker for seg in result.segments if seg.speaker and seg.speaker != "Unknown"))
    return {sp: SPEAKER_HEX_COLORS[i % len(SPEAKER_HEX_COLORS)] for i, sp in enumerate(speakers)}


def format_transcript_text(result: TranscriptionResult, speaker_names: dict[str, str], include_speakers: bool) -> str:
    """Plain-text transcript (for copy & TXT export)."""
    lines = []
    for seg in result.segments:
        ts = f"[{format_time(seg.start)}]"
        if include_speakers and seg.speaker:
            name = speaker_names.get(seg.speaker, seg.speaker)
            lines.append(f"{ts} {name}: {seg.text.strip()}")
        else:
            lines.append(f"{ts} {seg.text.strip()}")
    return "\n".join(lines)


def format_transcript_html(
    result: TranscriptionResult,
    speaker_names: dict[str, str],
    include_speakers: bool,
    search_query: str = "",
    speaker_filter: Optional[list[str]] = None,
) -> tuple[str, int]:
    """Colored HTML transcript with optional search highlight and speaker filter.

    Each segment carries data-idx (position in result.segments) and data-start (seconds)
    so JS can dispatch click events to the audio player and edit panel.

    Returns (html, match_count). match_count is the number of search matches found,
    or -1 when no search is active.
    """
    if not result:
        return "", -1

    colors = speaker_color_map(result)
    query = (search_query or "").strip()
    has_search = bool(query)
    filter_set = set(speaker_filter) if speaker_filter else None

    rows = []
    rows.append(
        '<div id="mt-transcript" style="font-family: ui-monospace, Menlo, Consolas, monospace; '
        'line-height: 1.7; padding: 14px; background: var(--block-background-fill, transparent); '
        'border-radius: 8px; max-height: 600px; overflow-y: auto;">'
    )

    match_count = 0
    visible_count = 0

    for idx, seg in enumerate(result.segments):
        # Speaker filter
        if filter_set is not None and seg.speaker not in filter_set:
            continue

        ts = format_time(seg.start)
        seg_text = seg.text.strip()
        text_escaped = escape(seg_text)

        # Highlight search matches (case-insensitive)
        if has_search:
            try:
                pattern = re.compile(re.escape(query), re.IGNORECASE)
                seg_matches = len(pattern.findall(seg_text))
                if seg_matches == 0:
                    continue
                match_count += seg_matches
                parts = pattern.split(seg_text)
                matches = pattern.findall(seg_text)
                rebuilt = []
                for i, part in enumerate(parts):
                    rebuilt.append(escape(part))
                    if i < len(matches):
                        rebuilt.append(f'<mark style="background:#fde68a;color:#1f2937;padding:0 2px;border-radius:2px;">{escape(matches[i])}</mark>')
                text_escaped = "".join(rebuilt)
            except re.error:
                pass

        speaker_html = ""
        if include_speakers and seg.speaker:
            name = escape(speaker_names.get(seg.speaker, seg.speaker))
            color = colors.get(seg.speaker, "#666")
            speaker_html = f'<strong style="color: {color};">{name}:</strong> '

        rows.append(
            f'<div class="mt-segment" data-idx="{idx}" data-start="{seg.start:.3f}" '
            f'style="margin-bottom: 6px; padding: 4px 6px; border-radius: 4px; cursor: pointer;" '
            f'title="Click to seek audio &middot; double-click to edit">'
            f'<span style="color: #94a3b8; font-size: 0.85em;">[{ts}]</span> '
            f'{speaker_html}'
            f'<span>{text_escaped}</span>'
            f'</div>'
        )
        visible_count += 1

    if visible_count == 0:
        rows.append('<div style="color:#94a3b8;text-align:center;padding:20px;">No matching segments.</div>')

    rows.append('</div>')
    return "".join(rows), (match_count if has_search else -1)


def compute_speaker_stats(
    result: TranscriptionResult,
    speaker_names: Optional[dict[str, str]] = None,
    voice_matches: Optional[dict[str, tuple[str, float]]] = None,
) -> list[list]:
    """Returns list of [speaker_id, display_name, utterances, speaking_time, share, match] rows.

    `voice_matches[label] = (matched_name, similarity)` adds a confidence column
    when a saved voice was recognized for that speaker.
    """
    if not result:
        return []
    stats: dict[str, dict] = {}
    for seg in result.segments:
        if seg.speaker and seg.speaker != "Unknown":
            if seg.speaker not in stats:
                stats[seg.speaker] = {"count": 0, "time": 0.0}
            stats[seg.speaker]["count"] += 1
            stats[seg.speaker]["time"] += seg.end - seg.start

    total_time = sum(s["time"] for s in stats.values()) or 1.0
    names = speaker_names or {}
    matches = voice_matches or {}
    rows = []
    for speaker in sorted(stats.keys()):
        s = stats[speaker]
        pct = s["time"] / total_time * 100
        display = names.get(speaker, speaker)
        match_cell = ""
        if speaker in matches:
            _name, sim = matches[speaker]
            match_cell = f"{sim * 100:.0f}%"
        rows.append([speaker, display, s["count"], format_time(s["time"]), f"{pct:.1f}%", match_cell])
    return rows


def render_steps_html(active_idx: int = -1, done: bool = False, elapsed: Optional[float] = None) -> str:
    """Render pipeline step badges + elapsed time as HTML."""
    parts = []
    for i, step in enumerate(PIPELINE_STEPS):
        if done:
            style = "background:#2ecc71;color:white;"
            label = f"&#10003; {step}"
        elif i < active_idx:
            style = "background:#2ecc71;color:white;"
            label = f"&#10003; {step}"
        elif i == active_idx:
            style = "background:#3b82f6;color:white;font-weight:bold;"
            label = f"&#9654; {step}"
        else:
            style = "background:#e5e7eb;color:#6b7280;"
            label = step

        parts.append(
            f'<span style="{style}display:inline-block;padding:4px 14px;'
            f'border-radius:14px;margin:0 3px;font-size:13px;">{label}</span>'
        )
        if i < len(PIPELINE_STEPS) - 1:
            parts.append('<span style="color:#9ca3af;font-size:12px;">&#x2500;&#x2500;</span>')

    elapsed_html = ""
    if elapsed is not None:
        elapsed_html = f'<div style="text-align:center;color:#6b7280;font-size:12px;margin-top:4px;">Elapsed: {format_time(elapsed)}</div>'

    return f'<div style="text-align:center;padding:8px 0;">{"".join(parts)}</div>{elapsed_html}'


def extract_date_from_filename(filepath: str) -> Optional[str]:
    if not filepath:
        return None
    filename = Path(filepath).stem
    patterns = [
        (r'(\d{4})[-_.](\d{2})[-_.](\d{2})', lambda m: f"{m.group(1)}-{m.group(2)}-{m.group(3)}"),
        (r'(\d{2})[-_.](\d{2})[-_.](\d{4})', lambda m: f"{m.group(3)}-{m.group(2)}-{m.group(1)}"),
        (r'(\d{4})(\d{2})(\d{2})', lambda m: f"{m.group(1)}-{m.group(2)}-{m.group(3)}"),
    ]
    for pattern, formatter in patterns:
        match = re.search(pattern, filename)
        if match:
            try:
                date_str = formatter(match)
                datetime.strptime(date_str, "%Y-%m-%d")
                return date_str
            except (ValueError, AttributeError):
                continue
    return None


# ── Speaker thumbnails ──────────────────────────────────────────────


def extract_speaker_thumbnails(
    video_path: str, result: TranscriptionResult
) -> list[tuple[str, str]]:
    """Extract one video frame per speaker. Returns list of (filepath, caption)."""
    if not video_path or is_audio_only(video_path) or not result:
        return []

    speakers = sorted(set(seg.speaker for seg in result.segments if seg.speaker and seg.speaker != "Unknown"))
    if not speakers:
        return []

    extractor = AudioExtractor()
    thumbs = []
    for speaker in speakers:
        try:
            first_segment = next(
                (seg for seg in result.segments if seg.speaker == speaker), None
            )
            if first_segment:
                timestamp = (first_segment.start + first_segment.end) / 2
                frame_path = os.path.join(
                    tempfile.gettempdir(),
                    f"speaker_{speaker.replace(' ', '_')}_{int(timestamp)}.png",
                )
                extractor.extract_frame(video_path, timestamp, frame_path)
                thumbs.append((frame_path, speaker))
        except Exception as e:
            logger.warning(f"Failed to extract frame for {speaker}: {e}")
    return thumbs


# ── Export file generation ──────────────────────────────────────────


def _build_filename(client: str, project: str, meeting_date: str, ext: str) -> str:
    parts = []
    if client:
        parts.append(client.replace(" ", "_"))
    if project:
        parts.append(project.replace(" ", "_"))
    if meeting_date:
        parts.append(meeting_date)
    base = "_".join(parts) + "_transcript" if parts else "transcript"
    return f"{base}.{ext}"


def _build_txt_header(
    result: TranscriptionResult,
    speaker_names: dict[str, str],
    include_speakers: bool,
    client: str,
    project: str,
    meeting_date: str,
) -> str:
    header_lines = ["Meeting Transcription", "=" * 40]
    if client:
        header_lines.append(f"Client: {client}")
    if project:
        header_lines.append(f"Project: {project}")
    if meeting_date:
        header_lines.append(f"Date: {meeting_date}")
    if result.duration:
        header_lines.append(f"Duration: {format_time(result.duration)}")
    if result.language:
        header_lines.append(f"Language: {result.language}")
    if include_speakers:
        speakers = set(seg.speaker for seg in result.segments if seg.speaker)
        if speakers:
            header_lines.append(f"Speakers: {len(speakers)}")
            for original in sorted(speakers):
                display = speaker_names.get(original, original)
                if display != original:
                    header_lines.append(f"  - {original} → {display}")
                else:
                    header_lines.append(f"  - {display}")
    header_lines.extend(["", "=" * 40, ""])
    return "\n".join(header_lines)


def export_file(
    fmt: str,
    result: TranscriptionResult,
    speaker_names: dict[str, str],
    include_speakers: bool,
    client: str,
    project: str,
    meeting_date: str,
) -> str:
    """Generate a download file in the requested format. Returns file path."""
    fmt = fmt.lower()
    output_path = os.path.join(
        tempfile.gettempdir(), _build_filename(client, project, meeting_date, fmt)
    )

    if fmt == "txt":
        header = _build_txt_header(result, speaker_names, include_speakers, client, project, meeting_date)
        body = format_transcript_text(result, speaker_names, include_speakers)
        with open(output_path, "w", encoding="utf-8") as f:
            f.write(header + body)
    elif fmt == "srt":
        with open(output_path, "w", encoding="utf-8") as f:
            f.write(to_srt(result, speaker_names, include_speakers))
    elif fmt == "vtt":
        with open(output_path, "w", encoding="utf-8") as f:
            f.write(to_vtt(result, speaker_names, include_speakers))
    elif fmt == "docx":
        write_docx(result, speaker_names, include_speakers, client, project, meeting_date, output_path)
    else:
        raise ValueError(f"Unknown format: {fmt}")

    return output_path


# ── Main transcription pipeline ─────────────────────────────────────


def transcribe_pipeline(
    file_obj,
    client: str,
    project: str,
    meeting_date: str,
    model_size: str,
    engine: str,
    language: str,
    diarization: bool,
    hf_token: str,
    condition_prev: bool,
    diar_model: str,
    export_fmt: str,
    initial_prompt: str,
    recognize_voices: bool,
    voice_threshold: float,
    recording_sel: str = "",
    user_label: str = "You",
):
    """Run the full transcription pipeline. Yields intermediate updates.

    Heavy work runs in a background thread; the generator polls a queue every 0.5s
    so the elapsed-time counter keeps ticking while a step is in progress.
    """
    save_config(
        model_size, engine, language, diarization, hf_token,
        condition_prev, diar_model, client, project, initial_prompt,
        recognize_voices, voice_threshold, user_label,
    )

    # Persist per-project settings (only if both fields are filled)
    if client and project:
        try:
            projects.save_settings(client, project, {
                "language": language,
                "model_size": model_size,
                "engine": engine,
                "diarization": diarization,
                "condition_on_previous_text": condition_prev,
                "diar_model": diar_model,
                "initial_prompt": initial_prompt,
            })
        except Exception as e:
            logger.warning(f"Failed to save project settings: {e}")

    empty = (render_steps_html(-1), "", "", [], [], "", None, {}, None, None, None)

    # A dual-track recording takes precedence over the upload box: it carries
    # strictly more information (which of the two tracks each utterance came
    # from), so there is no reason to fall back to the mixdown when both exist.
    dual: "recordings.Recording | None" = None
    if recording_sel:
        dual = recordings.find(recording_sel)
        if dual is None:
            gr.Warning(f"Recording '{recording_sel}' not found.")
            yield empty
            return

    if dual is None and file_obj is None:
        gr.Warning("Please upload a media file or pick a recording.")
        yield empty
        return

    file_path = None if dual else (file_obj if isinstance(file_obj, str) else file_obj.name)
    language_norm = language.strip() or None
    initial_prompt_norm = initial_prompt.strip() or None
    pipeline_start = time.time()

    def elapsed():
        return time.time() - pipeline_start

    def progress_only(steps_html):
        return (steps_html, "", "", [], [], "", None, {}, None, None, None)

    event_q: "queue.Queue" = queue.Queue()

    def worker():
        timings: dict[str, float] = {}
        try:
            # Step 1: Audio extraction
            event_q.put(("step", 0))
            t0 = time.time()
            if dual:
                # The tracks are already 16kHz mono and aligned by the recorder;
                # transcription still runs on the sum so overlapping speech reads
                # the same as it always did.
                audio_path = str(recordings.mix_tracks(
                    dual, Path(tempfile.gettempdir()) / f"mix_{dual.name}.wav"))
            else:
                extractor = AudioExtractor()
                audio_path = extractor.extract(file_path)
            timings["Audio extraction"] = time.time() - t0

            # Step 2: Load model
            event_q.put(("step", 1))
            if engine == "faster-whisper":
                transcriber = FasterWhisperTranscriber(model_size=model_size)
            elif engine == "WhisperX":
                transcriber = WhisperXTranscriber(model_size=model_size)
            else:
                transcriber = WhisperTranscriber(model_size=model_size)
            transcriber.load_model()

            # Step 3: Transcription
            event_q.put(("step", 2))
            t0 = time.time()
            result = transcriber.transcribe(
                audio_path,
                language=language_norm,
                condition_on_previous_text=condition_prev,
                initial_prompt=initial_prompt_norm,
            )
            timings["Transcription"] = time.time() - t0

            # The stages run sequentially and nothing below needs the ASR weights.
            # Keeping them resident forces diarization to share the card; on a small
            # GPU that spills into host memory over PCIe instead of failing loudly,
            # which shows up as a mysteriously slow diarization step.
            transcriber.unload_model()
            transcriber = None

            # Step 4: Diarization (optional)
            if diarization:
                event_q.put(("step", 3))
                t0 = time.time()
                try:
                    token = hf_token.strip() if hf_token else None
                    diarizer = SpeakerDiarizer(hf_token=token, model=diar_model)
                    diarizer.load_model()
                    # With two tracks, diarize only the system side: the mic
                    # track is known to be the user, so making pyannote guess at
                    # it only creates opportunities to confuse them.
                    diar_target = str(dual.system) if dual else audio_path
                    diar_segments = diarizer.diarize(diar_target)
                    result = diarizer.assign_speakers(result, diar_segments)
                    if dual:
                        mine, total = recordings.assign_owner(
                            result, dual, user_label=user_label or "You")
                        timings["Own-voice tagging"] = 0.0
                        logger.info(f"dual-track: {mine}/{total} segments tagged "
                                    f"as '{user_label}'")
                    timings["Diarization"] = time.time() - t0
                except Exception as e:
                    logger.warning(f"Diarization failed: {e}")
                    event_q.put(("warn", f"Diarization failed: {e}. Continuing without speakers."))
                finally:
                    # Voice fingerprinting loads its own embedding model next.
                    try:
                        diarizer.unload_model()
                    except (NameError, AttributeError):
                        pass

            # Step 5: Output
            event_q.put(("step", 4))

            speaker_names = {}
            for seg in result.segments:
                if seg.speaker and seg.speaker != "Unknown":
                    speaker_names[seg.speaker] = seg.speaker

            # Voice fingerprinting: try to match each detected speaker against
            # saved voice profiles and pre-fill the display name on a hit.
            voice_matches: dict[str, tuple[str, float]] = {}
            if diarization and recognize_voices:
                try:
                    by_speaker = _segments_by_speaker(result)
                    if by_speaker:
                        token = hf_token.strip() if hf_token else None
                        voice_matches = voices.match_speakers(
                            audio_path,
                            by_speaker,
                            hf_token=token,
                            threshold=voice_threshold,
                        )
                        for label, (matched_name, _sim) in voice_matches.items():
                            speaker_names[label] = matched_name
                        if voice_matches:
                            logger.info(f"Voice match: {len(voice_matches)} of {len(by_speaker)} speakers recognized")
                except Exception as e:
                    logger.warning(f"Voice matching failed: {e}")

            transcript_html, _ = format_transcript_html(result, speaker_names, diarization)
            transcript_text = format_transcript_text(result, speaker_names, diarization)
            stats = compute_speaker_stats(result, speaker_names=speaker_names, voice_matches=voice_matches)
            thumbs = extract_speaker_thumbnails(file_path, result) if (diarization and file_path) else []

            total = sum(timings.values())
            timing_lines = [f"{'Step':<22} {'Time':>8}", "─" * 32]
            for step, secs in timings.items():
                timing_lines.append(f"{step:<22} {secs:>7.1f}s")
            timing_lines.append("─" * 32)
            timing_lines.append(f"{'Total':<22} {total:>7.1f}s")
            timing_text = "\n".join(timing_lines)

            dl_path = export_file(export_fmt, result, speaker_names, diarization, client, project, meeting_date)

            # Persist to history (best-effort; never blocks the result)
            try:
                history.save_entry(
                    result=result,
                    speaker_names=speaker_names,
                    client=client,
                    project=project,
                    meeting_date=meeting_date,
                    audio_path=audio_path,
                    source_file=file_path or str(dual.path),
                )
            except Exception as e:
                logger.warning(f"History save failed: {e}")

            event_q.put(("done", (transcript_html, transcript_text, stats, thumbs, timing_text, result, speaker_names, dl_path, audio_path)))
        except Exception as e:
            logger.error(f"Transcription failed: {e}", exc_info=True)
            event_q.put(("error", str(e)))

    thread = threading.Thread(target=worker, daemon=True)
    thread.start()

    current_step = 0
    # Initial yield so UI updates immediately
    yield progress_only(render_steps_html(current_step, elapsed=elapsed()))

    while True:
        try:
            msg = event_q.get(timeout=0.5)
        except queue.Empty:
            # No event yet — refresh elapsed counter on the same step
            yield progress_only(render_steps_html(current_step, elapsed=elapsed()))
            continue

        kind = msg[0]
        if kind == "step":
            current_step = msg[1]
            yield progress_only(render_steps_html(current_step, elapsed=elapsed()))
        elif kind == "warn":
            gr.Warning(msg[1])
        elif kind == "done":
            (transcript_html, transcript_text, stats, thumbs, timing_text,
             result, speaker_names, dl_path, audio_path) = msg[1]
            yield (
                render_steps_html(-1, done=True, elapsed=elapsed()),
                transcript_html,
                transcript_text,
                stats,
                thumbs,
                timing_text,
                result,
                speaker_names,
                dl_path,
                render_audio_html(audio_path),
                audio_path,  # audio_path_state
            )
            return
        elif kind == "error":
            gr.Warning(f"Transcription failed: {msg[1]}")
            yield empty
            return


# ── Speaker renaming ─────────────────────────────────────────────────


def update_speaker_names(
    speaker_data, result_state, names_state,
    client, project, meeting_date, export_fmt,
    audio_path_state, hf_token, recognize_voices,
):
    """Rebuild transcript views and download file with new names.

    Also learns voice profiles when recognize_voices is on and the user
    confirmed names that aren't auto-generated ("Speaker N").
    """
    if result_state is None:
        gr.Warning("No transcription result available.")
        return gr.update(), gr.update(), names_state, gr.update()

    rows = []
    if hasattr(speaker_data, "values"):
        rows = speaker_data.values.tolist()
    elif isinstance(speaker_data, dict):
        rows = speaker_data.get("data", [])
    elif isinstance(speaker_data, list):
        rows = speaker_data

    if not rows:
        return gr.update(), gr.update(), names_state, gr.update()

    new_names = {}
    for row in rows:
        if len(row) >= 2:
            original = str(row[0]).strip()
            custom = str(row[1]).strip() if row[1] else original
            if original:
                new_names[original] = custom

    if not new_names:
        return gr.update(), gr.update(), names_state, gr.update()

    # Learn voice profiles for any speaker the user gave a real name to
    if recognize_voices and audio_path_state:
        try:
            by_speaker = _segments_by_speaker(result_state)
            token = hf_token.strip() if hf_token else None
            saved = voices.learn_speakers(audio_path_state, new_names, by_speaker, hf_token=token)
            if saved > 0:
                gr.Info(f"Saved voice profile for {saved} speaker{'s' if saved != 1 else ''}.")
        except Exception as e:
            logger.warning(f"Voice learning failed: {e}")

    transcript_html, _ = format_transcript_html(result_state, new_names, include_speakers=True)
    transcript_text = format_transcript_text(result_state, new_names, include_speakers=True)
    dl_path = export_file(export_fmt, result_state, new_names, True, client, project, meeting_date)
    return transcript_html, transcript_text, new_names, dl_path


def regenerate_export(export_fmt, result_state, names_state, client, project, meeting_date):
    """Regenerate the download file in a different format."""
    if result_state is None:
        return gr.update()
    include_speakers = bool(names_state)
    return export_file(export_fmt, result_state, names_state or {}, include_speakers, client, project, meeting_date)


def get_speaker_choices(result_state) -> list[str]:
    """Return sorted list of distinct speakers in the current result."""
    if result_state is None:
        return []
    return sorted(set(
        seg.speaker for seg in result_state.segments
        if seg.speaker and seg.speaker != "Unknown"
    ))


def filter_and_search(search_query, speaker_filter, result_state, names_state):
    """Apply search highlight + speaker filter to the transcript HTML."""
    if result_state is None:
        return gr.update(), ""

    html, count = format_transcript_html(
        result_state,
        names_state or {},
        include_speakers=True,
        search_query=search_query,
        speaker_filter=speaker_filter,
    )

    if count > 0:
        status = f"**{count}** match{'es' if count != 1 else ''} found"
    elif count == 0:
        status = "No matches"
    else:
        status = ""

    return html, status


def refresh_speaker_filter(result_state):
    """Update the multi-select speaker filter when result changes."""
    choices = get_speaker_choices(result_state)
    return gr.update(choices=choices, value=choices)


def refresh_merge_dropdowns(result_state):
    """Update the merge dropdowns whenever the result changes."""
    choices = get_speaker_choices(result_state)
    return (
        gr.update(choices=choices, value=choices[0] if choices else None),
        gr.update(choices=choices, value=choices[1] if len(choices) > 1 else None),
    )


def merge_speakers(
    from_speaker, into_speaker, result_state, names_state,
    client, project, meeting_date, export_fmt,
):
    """Merge from_speaker into into_speaker — assigns from's segments to into."""
    if result_state is None:
        gr.Warning("No transcription result available.")
        return tuple(gr.update() for _ in range(7))

    if not from_speaker or not into_speaker:
        gr.Warning("Select both speakers to merge.")
        return tuple(gr.update() for _ in range(7))

    if from_speaker == into_speaker:
        gr.Warning("Cannot merge a speaker into itself.")
        return tuple(gr.update() for _ in range(7))

    # Reassign segments in-place on result_state
    merged_count = 0
    for seg in result_state.segments:
        if seg.speaker == from_speaker:
            seg.speaker = into_speaker
            merged_count += 1

    # Drop the merged speaker from names_state
    new_names = dict(names_state or {})
    new_names.pop(from_speaker, None)
    if into_speaker not in new_names:
        new_names[into_speaker] = into_speaker

    # Regenerate everything that depends on speakers
    transcript_html, _ = format_transcript_html(result_state, new_names, include_speakers=True)
    transcript_text = format_transcript_text(result_state, new_names, include_speakers=True)
    stats = compute_speaker_stats(result_state)
    dl_path = export_file(export_fmt, result_state, new_names, True, client, project, meeting_date)

    choices = get_speaker_choices(result_state)
    gr.Info(f"Merged {merged_count} segments from {from_speaker} into {into_speaker}.")

    return (
        transcript_html,
        transcript_text,
        stats,
        new_names,
        dl_path,
        gr.update(choices=choices, value=choices[0] if choices else None),
        gr.update(choices=choices, value=choices[1] if len(choices) > 1 else None),
    )


# ── Segment editing ──────────────────────────────────────────────────


def open_segment_editor(idx_str, result_state):
    """Load a segment's data into the edit panel and show it.

    The hidden input receives values like "3:1761234567890" — the timestamp suffix
    forces a change event even when the same segment is double-clicked twice.
    """
    if result_state is None or not idx_str:
        return tuple(gr.update() for _ in range(5))

    raw = str(idx_str).split(":", 1)[0]
    try:
        idx = int(float(raw))
    except (ValueError, TypeError):
        return tuple(gr.update() for _ in range(5))

    if idx < 0 or idx >= len(result_state.segments):
        return tuple(gr.update() for _ in range(5))

    seg = result_state.segments[idx]
    speakers = get_speaker_choices(result_state)
    current_speaker = seg.speaker if seg.speaker in speakers else (speakers[0] if speakers else None)

    label = f"Editing segment {idx + 1} &middot; {format_time(seg.start)}–{format_time(seg.end)}"

    return (
        gr.update(visible=True),                                # editor_panel
        gr.update(value=seg.text.strip()),                      # editor_text
        gr.update(choices=speakers, value=current_speaker),     # editor_speaker
        gr.update(value=label),                                 # editor_header
        idx,                                                    # editor_idx_state
    )


def save_segment_edit(
    editor_idx, editor_text, editor_speaker,
    result_state, names_state,
    client, project, meeting_date, export_fmt,
    search_query, speaker_filter,
):
    """Apply edits to a single segment, regenerate views and download."""
    if result_state is None or editor_idx is None:
        return tuple(gr.update() for _ in range(6)) + (gr.update(visible=False),)

    try:
        idx = int(editor_idx)
    except (ValueError, TypeError):
        return tuple(gr.update() for _ in range(6)) + (gr.update(visible=False),)

    if idx < 0 or idx >= len(result_state.segments):
        return tuple(gr.update() for _ in range(6)) + (gr.update(visible=False),)

    seg = result_state.segments[idx]
    new_text = (editor_text or "").strip()
    if new_text:
        seg.text = new_text
    if editor_speaker:
        seg.speaker = editor_speaker

    # Regenerate dependent views (respecting active search/filter)
    transcript_html, _ = format_transcript_html(
        result_state, names_state or {}, include_speakers=True,
        search_query=search_query, speaker_filter=speaker_filter,
    )
    transcript_text = format_transcript_text(result_state, names_state or {}, include_speakers=True)
    stats = compute_speaker_stats(result_state)
    dl_path = export_file(export_fmt, result_state, names_state or {}, True, client, project, meeting_date)

    return (
        transcript_html,
        transcript_text,
        stats,
        names_state,
        dl_path,
        result_state,                # write back so result_state.change fires for downstream listeners
        gr.update(visible=False),    # hide editor
    )


def close_segment_editor():
    """Hide the edit panel without saving."""
    return gr.update(visible=False)


# ── History handlers ─────────────────────────────────────────────────


def _format_history_row(entry: dict) -> list:
    """Convert a history entry summary into a dataframe row."""
    saved_dt = datetime.fromtimestamp(entry["saved_at"]).strftime("%Y-%m-%d %H:%M")
    duration = format_time(entry["duration"]) if entry.get("duration") else ""
    return [
        entry["id"],
        saved_dt,
        entry.get("client", "") or "—",
        entry.get("project", "") or "—",
        entry.get("date", "") or "—",
        duration,
        entry.get("n_segments", 0),
        entry.get("n_speakers", 0),
    ]


def refresh_history_table():
    """Load the history table fresh from disk."""
    rows = [_format_history_row(e) for e in history.list_entries()]
    return rows


def remember_history_selection(evt: gr.SelectData):
    """Store the row clicked on the history table."""
    if evt is None or evt.index is None:
        return None
    return evt.index[0] if isinstance(evt.index, (list, tuple)) else evt.index


def _row_id_from_table(history_data, row_idx) -> Optional[str]:
    if row_idx is None:
        return None
    rows = []
    if hasattr(history_data, "values"):
        rows = history_data.values.tolist()
    elif isinstance(history_data, dict):
        rows = history_data.get("data", [])
    elif isinstance(history_data, list):
        rows = history_data
    if not rows or row_idx >= len(rows):
        return None
    return str(rows[row_idx][0])


def load_history_entry(history_data, row_idx, export_fmt):
    """Restore a transcription from history into the active UI state."""
    n_outputs = 11
    no_op = tuple(gr.update() for _ in range(n_outputs))

    entry_id = _row_id_from_table(history_data, row_idx)
    if entry_id is None:
        gr.Warning("Select a row first.")
        return no_op

    payload = history.load_entry(entry_id)
    if payload is None:
        gr.Warning(f"History entry {entry_id} not found.")
        return no_op

    result = history.entry_to_result(payload)
    speaker_names = payload.get("speaker_names") or {}
    audio_path = payload.get("audio_path")

    transcript_html, _ = format_transcript_html(result, speaker_names, include_speakers=True)
    transcript_text = format_transcript_text(result, speaker_names, include_speakers=True)
    stats = compute_speaker_stats(result)

    h_client = payload.get("client") or ""
    h_project = payload.get("project") or ""
    h_date = payload.get("date") or ""

    dl_path = export_file(export_fmt, result, speaker_names, True, h_client, h_project, h_date)

    saved_str = datetime.fromtimestamp(payload["saved_at"]).strftime("%Y-%m-%d %H:%M")
    gr.Info(f"Loaded transcription from {saved_str}.")

    return (
        transcript_html,        # transcript_html
        transcript_text,        # transcript_text_box
        stats,                  # speaker_table
        result,                 # result_state
        speaker_names,          # names_state
        dl_path,                # download_file
        render_audio_html(audio_path),  # audio_player
        gr.update(value=h_client),    # client_input
        gr.update(value=h_project),   # project_input
        gr.update(value=h_date),      # date_input
        render_steps_html(-1, done=True),  # steps_html
    )


def delete_history_entry(history_data, row_idx):
    """Delete the selected history row and refresh the table."""
    entry_id = _row_id_from_table(history_data, row_idx)
    if entry_id is None:
        gr.Warning("Select a row first.")
        return refresh_history_table(), None
    if history.delete_entry(entry_id):
        gr.Info("Deleted history entry.")
    return refresh_history_table(), None


# ── Client/Project handlers ──────────────────────────────────────────


def on_client_change(client):
    """When client changes, refresh the projects dropdown."""
    return gr.update(choices=projects.list_projects(client), value=None)


def on_project_change(client, project):
    """When project is selected, load its settings into the UI fields.

    Returns updates for: model_input, engine_input, language_input,
    diarization_input, condition_prev_input, diar_model_input, initial_prompt_input.
    Fields with no saved value receive a no-op update.
    """
    settings = projects.get_settings(client, project) if client and project else None

    def upd(value):
        return gr.update(value=value) if value is not None else gr.update()

    if settings is None:
        # No-op: keep current UI values
        return tuple(gr.update() for _ in range(7))

    return (
        upd(settings.get("model_size")),
        upd(settings.get("engine")),
        upd(settings.get("language") or ""),  # empty string is valid for the textbox
        upd(settings.get("diarization")),
        upd(settings.get("condition_on_previous_text")),
        upd(settings.get("diar_model")),
        upd(settings.get("initial_prompt") or ""),
    )


def delete_current_client(client):
    """Delete the currently-selected client and all its projects."""
    if not client:
        gr.Warning("No client selected.")
        return gr.update(), gr.update()
    if projects.delete_client(client):
        gr.Info(f"Deleted client '{client}' and its projects.")
    return (
        gr.update(choices=projects.list_clients(), value=None),
        gr.update(choices=[], value=None),
    )


def delete_current_project(client, project):
    """Delete the currently-selected project under the current client."""
    if not client or not project:
        gr.Warning("Select both a client and a project to delete.")
        return gr.update()
    if projects.delete_project(client, project):
        gr.Info(f"Deleted project '{project}'.")
    return gr.update(choices=projects.list_projects(client), value=None)


# ── Voice profile handlers ──────────────────────────────────────────


def _voice_row(entry: dict) -> list:
    created = datetime.fromtimestamp(entry["created_at"]).strftime("%Y-%m-%d") if entry.get("created_at") else "—"
    updated = datetime.fromtimestamp(entry["updated_at"]).strftime("%Y-%m-%d %H:%M") if entry.get("updated_at") else "—"
    return [entry["name"], entry["samples"], created, updated]


def refresh_voices_table():
    return [_voice_row(e) for e in voices.list_profiles()]


def remember_voice_selection(evt: gr.SelectData):
    if evt is None or evt.index is None:
        return None
    return evt.index[0] if isinstance(evt.index, (list, tuple)) else evt.index


def delete_voice_profile(voices_data, row_idx):
    if row_idx is None:
        gr.Warning("Select a row first.")
        return refresh_voices_table(), None
    rows = []
    if hasattr(voices_data, "values"):
        rows = voices_data.values.tolist()
    elif isinstance(voices_data, dict):
        rows = voices_data.get("data", [])
    elif isinstance(voices_data, list):
        rows = voices_data
    if not rows or row_idx >= len(rows):
        return refresh_voices_table(), None
    name = str(rows[row_idx][0])
    if voices.delete_profile(name):
        gr.Info(f"Deleted voice profile for '{name}'.")
    return refresh_voices_table(), None


# ── File change handler ──────────────────────────────────────────────


def on_file_change(file_obj):
    if file_obj is None:
        return gr.update()
    file_path = file_obj if isinstance(file_obj, str) else file_obj.name
    date = extract_date_from_filename(file_path)
    if date:
        return gr.update(value=date)
    return gr.update()


def on_recording_change(recording_sel: str, vocabulary: str = ""):
    """Preenche data e vocabulário ao escolher uma gravação.

    A data vem de `recorded_at` no meta.json e não do nome da pasta: é o
    instante real e sobrevive a um rename. O vocabulário recebe os
    participantes que o gravador colheu da agenda -- nomes próprios são a
    maior fonte de erro da transcrição, e aqui eles chegam de graça.
    """
    rec = recordings.find(recording_sel or "")
    if rec is None:
        return gr.update(), gr.update()

    vocab = rec.merge_vocabulary(vocabulary)
    vocab_update = (gr.update(value=vocab) if vocab != (vocabulary or "").strip()
                    else gr.update())
    stamp = rec.meta.get("recorded_at")
    if stamp:
        try:
            # ISO em UTC -> data local, que e o que o usuario reconhece.
            dt = datetime.fromisoformat(stamp)
            if dt.tzinfo is not None:
                dt = dt.astimezone()
            return gr.update(value=dt.strftime("%Y-%m-%d")), vocab_update
        except ValueError:
            logger.debug(f"recorded_at ilegivel em {rec.name}: {stamp!r}")
    date = extract_date_from_filename(rec.name)
    return (gr.update(value=date) if date else gr.update()), vocab_update


# ── Build Gradio app ─────────────────────────────────────────────────


def get_build_label() -> str:
    """Identify the running code, so a stale container is visible at a glance.

    Docker bakes /app/BUILD_INFO at build time. Running from source there is no
    stamp, so fall back to the current git commit.
    """
    stamp = Path("/app/BUILD_INFO")
    if stamp.is_file():
        try:
            text = stamp.read_text().strip()
            if text:
                return f"build {text}"
        except OSError:
            pass

    try:
        sha = subprocess.run(
            ["git", "rev-parse", "--short", "HEAD"],
            capture_output=True, text=True, timeout=5,
            cwd=Path(__file__).resolve().parents[2],
        )
        if sha.returncode == 0 and sha.stdout.strip():
            return f"source @ {sha.stdout.strip()}"
    except (OSError, subprocess.SubprocessError):
        pass

    return "source (unversioned)"


def create_app() -> gr.Blocks:
    enable_gpu_optimizations()

    gpu_available = is_cuda_available()
    default_engine = "faster-whisper" if gpu_available else "Whisper"
    gpu_label = "GPU" if gpu_available else "CPU only"

    config = load_config()
    logo_svg = get_logo_inline_svg()
    build_label = get_build_label()
    logger.info(f"Running {build_label}")

    # Header HTML with inline logo
    header_html = f"""
    <div style="display: flex; align-items: center; gap: 16px; padding: 8px 0; margin-bottom: 8px;">
        <div style="width: 56px; height: 56px; flex-shrink: 0; color: var(--body-text-color, currentColor);">
            {logo_svg}
        </div>
        <div>
            <h1 style="margin: 0; font-size: 1.6rem;">Meeting Transcription</h1>
            <div style="color: #94a3b8; font-size: 0.9rem;">Transcribe meetings with speaker diarization &mdash; <strong>{gpu_label}</strong> &middot; <span title="Which code this container is running. After a rebuild, recreate the container (docker compose up -d) or this will not change.">{escape(build_label)}</span></div>
        </div>
    </div>
    """

    # JS attached on load:
    #   1. Keyboard shortcuts (Ctrl+Enter, Esc)
    #   2. Click on a segment → seek the audio
    #   3. Double-click on a segment → open the edit panel via hidden number input
    keyboard_js = """
    () => {
        if (window.__mt_kbd_attached) return;
        window.__mt_kbd_attached = true;

        document.addEventListener('keydown', (e) => {
            const target = e.target;
            const isEditable = target.matches('input, textarea, [contenteditable]');
            if (e.ctrlKey && e.key === 'Enter') {
                e.preventDefault();
                document.getElementById('mt-start-btn')?.click();
            } else if (e.key === 'Escape' && !isEditable) {
                const editor = document.getElementById('mt-editor-panel');
                const editorVisible = editor && editor.offsetParent !== null;
                if (editorVisible) {
                    e.preventDefault();
                    document.getElementById('mt-editor-cancel-btn')?.click();
                } else {
                    e.preventDefault();
                    document.getElementById('mt-cancel-btn')?.click();
                }
            }
        });

        const findSegment = (target) => target.closest && target.closest('.mt-segment');
        const findAudio = () => {
            const own = document.getElementById('mt-audio-real');
            if (own) return own;
            return document.querySelector('audio, video');
        };
        const findHiddenIdxInput = () => {
            const wrapper = document.getElementById('mt-segment-idx');
            if (!wrapper) return null;
            return wrapper.querySelector('input, textarea');
        };

        const seekTo = (start) => {
            const audio = findAudio();
            if (!audio) {
                console.warn('[mt] No audio element on page');
                return false;
            }
            if (isNaN(start)) return false;
            try {
                audio.currentTime = start;
                if (typeof audio.fastSeek === 'function') audio.fastSeek(start);
                const p = audio.play();
                if (p && p.catch) p.catch(() => {});
                return true;
            } catch (err) {
                console.warn('[mt] seek failed:', err);
                return false;
            }
        };

        // Single click → seek audio to segment start
        document.addEventListener('click', (e) => {
            const seg = findSegment(e.target);
            if (!seg) return;
            const start = parseFloat(seg.dataset.start);
            seekTo(start);
            document.querySelectorAll('.mt-segment.mt-active').forEach(el => el.classList.remove('mt-active'));
            seg.classList.add('mt-active');
        });

        // Double click → open the edit panel via hidden textbox.
        // Gradio 6 listens to native 'input' events on the underlying textarea;
        // we set the value and dispatch both 'input' and 'change' to be safe.
        document.addEventListener('dblclick', (e) => {
            const seg = findSegment(e.target);
            if (!seg) {
                return;
            }
            const idx = seg.dataset.idx;
            const input = findHiddenIdxInput();
            if (!input) {
                console.warn('[mt] hidden #mt-segment-idx input not found');
                return;
            }
            if (idx === undefined) {
                console.warn('[mt] segment is missing data-idx');
                return;
            }
            const newValue = idx + ':' + Date.now();
            // Use the property setter Svelte/React track for proper change detection
            const proto = Object.getPrototypeOf(input);
            const setter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;
            if (setter) {
                setter.call(input, newValue);
            } else {
                input.value = newValue;
            }
            input.dispatchEvent(new Event('input', { bubbles: true }));
            input.dispatchEvent(new Event('change', { bubbles: true }));
        });
    }
    """

    with gr.Blocks(title="Meeting Transcription") as app:
        # State
        result_state = gr.State(None)
        names_state = gr.State({})
        audio_path_state = gr.State(None)  # raw path to extracted WAV (for voice fingerprinting)

        gr.HTML(header_html)

        # ── File Upload ──
        file_input = gr.File(
            label="Upload media file",
            file_types=[ext for ext in SUPPORTED_EXTENSIONS],
            type="filepath",
        )

        # ── Dual-track recordings from the Windows recorder ──
        with gr.Accordion("From the recorder (two tracks)", open=False):
            gr.Markdown(
                "Recordings made by the Windows tray recorder keep your mic and "
                "the meeting audio on separate tracks. Picking one here means "
                "your own speech is identified from the mic track instead of "
                "being guessed at by diarization."
            )
            with gr.Row():
                recording_input = gr.Dropdown(
                    label="Recording",
                    choices=[r.label() for r in recordings.list_recordings()],
                    value=None,
                    interactive=True,
                    scale=4,
                )
                refresh_recordings_btn = gr.Button("Refresh", scale=1)
            user_label_input = gr.Textbox(
                label="Your name on the mic track",
                value=config.get("user_label", "You"),
                placeholder="How segments from your microphone get labelled",
            )

        # ── Recent Transcriptions ──
        with gr.Accordion("Recent transcriptions", open=False):
            history_table = gr.Dataframe(
                headers=["ID", "Saved", "Client", "Project", "Date", "Duration", "Segments", "Speakers"],
                datatype=["str", "str", "str", "str", "str", "str", "number", "number"],
                interactive=False,
                value=refresh_history_table(),
                wrap=True,
            )
            with gr.Row():
                history_load_btn = gr.Button("Load selected", size="sm", variant="primary")
                history_delete_btn = gr.Button("Delete selected", size="sm", variant="stop")
                history_refresh_btn = gr.Button("Refresh", size="sm")
            history_selected_state = gr.State(None)

        # ── Meeting Info ──
        with gr.Accordion("Meeting Information", open=True):
            initial_client = config.get("client", "") or None
            with gr.Row():
                client_input = gr.Dropdown(
                    label="Client",
                    choices=projects.list_clients(),
                    value=initial_client,
                    allow_custom_value=True,
                    scale=2,
                )
                project_input = gr.Dropdown(
                    label="Project",
                    choices=projects.list_projects(initial_client) if initial_client else [],
                    value=config.get("project", "") or None,
                    allow_custom_value=True,
                    scale=2,
                )
                date_input = gr.Textbox(label="Date", placeholder="YYYY-MM-DD", scale=1)

            with gr.Accordion("Manage clients/projects", open=False):
                gr.Markdown("Type a new name in the dropdown to add &middot; selecting a saved project loads its settings.")
                with gr.Row():
                    delete_project_btn = gr.Button(
                        "Delete current project", size="sm", variant="stop",
                    )
                    delete_client_btn = gr.Button(
                        "Delete current client (and all projects)", size="sm", variant="stop",
                    )

        # ── Engine & Model ──
        with gr.Accordion("Engine & Model", open=True):
            with gr.Row():
                model_input = gr.Dropdown(
                    choices=MODEL_SIZES,
                    value=config.get("model_size", "base"),
                    label="Model Size",
                )
                engine_input = gr.Dropdown(
                    choices=["faster-whisper", "WhisperX", "Whisper"],
                    value=config.get("engine", default_engine),
                    label="Engine",
                )
                language_input = gr.Textbox(
                    label="Language",
                    placeholder="Auto (e.g. pt, en, es...)",
                    value=config.get("language", ""),
                )
            with gr.Row():
                diarization_input = gr.Checkbox(
                    label="Speaker Diarization",
                    value=config.get("diarization", True),
                )

        # ── Advanced Settings ──
        with gr.Accordion("Advanced Settings", open=False):
            with gr.Row():
                hf_token_input = gr.Textbox(
                    label="HuggingFace Token",
                    placeholder="Token for speaker diarization",
                    value=config.get("hf_token", ""),
                    type="password",
                )
                diar_model_input = gr.Dropdown(
                    choices=["community-1", "3.1"],
                    value=config.get("diar_model", "community-1"),
                    label="Diarization Model",
                )
            condition_prev_input = gr.Checkbox(
                label="Condition on previous text (uncheck reduces hallucinations)",
                value=config.get("condition_on_previous_text", False),
            )
            initial_prompt_input = gr.Textbox(
                label="Custom vocabulary / context",
                placeholder="Names, jargon, technical terms (e.g., 'Acme Corp, Project Atlas, Jane Smith, API REST'). Helps the model recognize domain-specific words.",
                info=(
                    "Only include words that are actually spoken out loud — table and "
                    "field names waste the budget. Keep it under ~220 tokens (roughly "
                    "150 words); anything beyond that is silently dropped."
                ),
                value=config.get("initial_prompt", ""),
                lines=2,
                max_lines=4,
            )
            with gr.Row():
                recognize_voices_input = gr.Checkbox(
                    label="Recognize voices (auto-suggest names from saved profiles)",
                    value=config.get("recognize_voices", True),
                )
                voice_threshold_input = gr.Slider(
                    minimum=0.5, maximum=0.9, step=0.01,
                    value=float(config.get("voice_threshold", 0.65)),
                    label="Voice match threshold",
                    info="Higher = stricter (fewer false positives, more 'Speaker N' surviving)",
                )

        # ── Action Buttons ──
        with gr.Row():
            start_btn = gr.Button(
                "Start Transcription (Ctrl+Enter)", variant="primary", size="lg",
                elem_id="mt-start-btn", scale=4,
            )
            cancel_btn = gr.Button(
                "Cancel (Esc)", variant="stop", size="lg",
                elem_id="mt-cancel-btn", scale=1,
            )

        # ── Pipeline Steps ──
        steps_html = gr.HTML(value=render_steps_html(-1))

        # ── Audio player (synced to transcript via JS) ──
        # Custom HTML5 audio bypasses gr.Audio's WaveSurfer (which uses Web Audio
        # API and makes programmatic seek impossible).
        gr.Markdown("**Audio player** &mdash; click a segment in the transcript to seek")
        audio_player = gr.HTML(
            value=render_audio_html(None),
            elem_id="mt-audio",
        )

        # Hidden input that JS writes to when a segment is double-clicked.
        # We use a visible textbox with CSS-hidden styling instead of visible=False
        # because Gradio's display:none can break event propagation from synthetic
        # JS dispatches.
        segment_idx_input = gr.Textbox(
            value="",
            label="",
            show_label=False,
            elem_id="mt-segment-idx",
            elem_classes=["mt-hidden-input"],
        )

        # ── Segment editor (hidden until a segment is double-clicked) ──
        with gr.Group(visible=False, elem_id="mt-editor-panel") as editor_panel:
            editor_header = gr.Markdown("**Editing segment**")
            with gr.Row():
                editor_text = gr.Textbox(
                    label="Text",
                    lines=2,
                    max_lines=6,
                    scale=3,
                )
                editor_speaker = gr.Dropdown(
                    label="Speaker",
                    choices=[],
                    scale=1,
                    allow_custom_value=False,
                )
            with gr.Row():
                editor_save_btn = gr.Button("Save", variant="primary", size="sm", elem_id="mt-editor-save-btn")
                editor_cancel_btn = gr.Button("Cancel", size="sm", elem_id="mt-editor-cancel-btn")

        editor_idx_state = gr.State(None)

        gr.Markdown(
            "<small>&middot; Click a segment to seek the audio &middot; Double-click to edit text or speaker</small>"
        )

        # ── Search + Speaker filter (Formatted view only) ──
        with gr.Row():
            search_input = gr.Textbox(
                label="Search in transcript",
                placeholder="Type to highlight matches...",
                scale=3,
            )
            speaker_filter = gr.CheckboxGroup(
                label="Show speakers",
                choices=[],
                value=[],
                scale=2,
            )
        search_status = gr.Markdown("")

        # ── Results: Tabs for Formatted vs Plain Text ──
        with gr.Tabs():
            with gr.Tab("Formatted"):
                transcript_html = gr.HTML(value="")
            with gr.Tab("Plain text"):
                transcript_text_box = gr.Textbox(
                    label="",
                    lines=15,
                    max_lines=40,
                    interactive=False,
                    buttons=["copy"],
                    show_label=False,
                )

        # ── Speaker Statistics + Thumbnails ──
        with gr.Accordion("Speakers", open=False):
            gr.Markdown("Edit the **Display Name** column to rename speakers, then click **Apply Names**.")
            with gr.Row():
                with gr.Column(scale=2):
                    speaker_table = gr.Dataframe(
                        headers=["Speaker ID", "Display Name", "Utterances", "Speaking Time", "Share", "Voice match"],
                        datatype=["str", "str", "number", "str", "str", "str"],
                        interactive=True,
                        column_count=(6, "fixed"),
                    )
                    apply_names_btn = gr.Button("Apply Names", size="sm")
                with gr.Column(scale=1):
                    speaker_gallery = gr.Gallery(
                        label="Speaker thumbnails (video files only)",
                        columns=2,
                        rows=2,
                        height=240,
                        show_label=True,
                        object_fit="cover",
                        allow_preview=True,
                    )

            gr.Markdown("**Merge speakers** &mdash; useful when diarization split the same person into multiple speakers.")
            with gr.Row():
                merge_from = gr.Dropdown(label="Merge", choices=[], scale=1, allow_custom_value=False)
                merge_into = gr.Dropdown(label="into", choices=[], scale=1, allow_custom_value=False)
                merge_btn = gr.Button("Merge", size="sm", scale=1)

        # ── Saved voices (voice fingerprinting) ──
        with gr.Accordion("Saved voices", open=False):
            gr.Markdown(
                "Voice profiles learned from past transcriptions. When you click **Apply Names** and "
                "the speaker has a real name (not 'Speaker N'), their voice fingerprint is saved. "
                "Future transcriptions will pre-fill recognized names automatically."
            )
            voices_table = gr.Dataframe(
                headers=["Name", "Samples", "First saved", "Last updated"],
                datatype=["str", "number", "str", "str"],
                interactive=False,
                column_count=(4, "fixed"),
            )
            with gr.Row():
                voices_refresh_btn = gr.Button("Refresh", size="sm")
                voices_delete_btn = gr.Button("Delete selected", size="sm", variant="stop")
            voices_selected_state = gr.State(None)

        # ── Performance ──
        with gr.Accordion("Performance", open=False):
            timing_output = gr.Textbox(
                label="Timings",
                interactive=False,
                lines=6,
                elem_classes=["timing-box"],
            )

        # ── Export ──
        with gr.Row():
            export_format = gr.Radio(
                choices=["TXT", "SRT", "VTT", "DOCX"],
                value="TXT",
                label="Export format",
                scale=4,
            )
        download_file = gr.File(label="Download Transcript", visible=True, interactive=False)

        # ── Events ──

        file_input.change(fn=on_file_change, inputs=[file_input], outputs=[date_input])

        # ── Client/Project events ──
        # When client changes → refresh project dropdown (and clear selection)
        client_input.change(
            fn=on_client_change,
            inputs=[client_input],
            outputs=[project_input],
        )

        # When project changes → load its saved settings into all fields
        project_input.change(
            fn=on_project_change,
            inputs=[client_input, project_input],
            outputs=[
                model_input, engine_input, language_input,
                diarization_input, condition_prev_input,
                diar_model_input, initial_prompt_input,
            ],
        )

        # Manage projects buttons
        delete_client_btn.click(
            fn=delete_current_client,
            inputs=[client_input],
            outputs=[client_input, project_input],
        )
        delete_project_btn.click(
            fn=delete_current_project,
            inputs=[client_input, project_input],
            outputs=[project_input],
        )

        start_event = start_btn.click(
            fn=transcribe_pipeline,
            inputs=[
                file_input,
                client_input,
                project_input,
                date_input,
                model_input,
                engine_input,
                language_input,
                diarization_input,
                hf_token_input,
                condition_prev_input,
                diar_model_input,
                export_format,
                initial_prompt_input,
                recognize_voices_input,
                voice_threshold_input,
                recording_input,
                user_label_input,
            ],
            outputs=[
                steps_html,
                transcript_html,
                transcript_text_box,
                speaker_table,
                speaker_gallery,
                timing_output,
                result_state,
                names_state,
                download_file,
                audio_player,
                audio_path_state,
            ],
        )

        # Cancel button — cancels the start event
        cancel_btn.click(fn=lambda: None, cancels=[start_event])

        # Re-scan the recordings folder without reloading the page: the recorder
        # writes into it while this app is up.
        refresh_recordings_btn.click(
            fn=lambda: gr.update(
                choices=[r.label() for r in recordings.list_recordings()]),
            outputs=[recording_input],
        )

        recording_input.change(
            fn=on_recording_change,
            inputs=[recording_input, initial_prompt_input],
            outputs=[date_input, initial_prompt_input])

        # Apply speaker name changes (also learns voice profiles)
        apply_names_btn.click(
            fn=update_speaker_names,
            inputs=[
                speaker_table, result_state, names_state,
                client_input, project_input, date_input, export_format,
                audio_path_state, hf_token_input, recognize_voices_input,
            ],
            outputs=[transcript_html, transcript_text_box, names_state, download_file],
        )

        # Re-export when format changes
        export_format.change(
            fn=regenerate_export,
            inputs=[export_format, result_state, names_state, client_input, project_input, date_input],
            outputs=[download_file],
        )

        # Populate merge dropdowns whenever result_state changes
        result_state.change(
            fn=refresh_merge_dropdowns,
            inputs=[result_state],
            outputs=[merge_from, merge_into],
        )

        # Populate speaker filter (and select all by default) when result changes
        result_state.change(
            fn=refresh_speaker_filter,
            inputs=[result_state],
            outputs=[speaker_filter],
        )

        # Refresh history table after each new transcription completes
        result_state.change(
            fn=refresh_history_table,
            outputs=[history_table],
        )

        # Search + filter events — update transcript HTML on input
        search_input.input(
            fn=filter_and_search,
            inputs=[search_input, speaker_filter, result_state, names_state],
            outputs=[transcript_html, search_status],
        )
        speaker_filter.change(
            fn=filter_and_search,
            inputs=[search_input, speaker_filter, result_state, names_state],
            outputs=[transcript_html, search_status],
        )

        # Merge speakers
        merge_btn.click(
            fn=merge_speakers,
            inputs=[
                merge_from, merge_into, result_state, names_state,
                client_input, project_input, date_input, export_format,
            ],
            outputs=[
                transcript_html, transcript_text_box, speaker_table,
                names_state, download_file, merge_from, merge_into,
            ],
        )

        # ── History events ──
        history_table.select(
            fn=remember_history_selection,
            inputs=None,
            outputs=[history_selected_state],
        )
        history_refresh_btn.click(
            fn=refresh_history_table,
            outputs=[history_table],
        )
        history_load_btn.click(
            fn=load_history_entry,
            inputs=[history_table, history_selected_state, export_format],
            outputs=[
                transcript_html, transcript_text_box, speaker_table,
                result_state, names_state, download_file, audio_player,
                client_input, project_input, date_input, steps_html,
            ],
        )
        history_delete_btn.click(
            fn=delete_history_entry,
            inputs=[history_table, history_selected_state],
            outputs=[history_table, history_selected_state],
        )

        # ── Segment editor events ──
        # Hidden input changes when JS dispatches a double-click
        segment_idx_input.change(
            fn=open_segment_editor,
            inputs=[segment_idx_input, result_state],
            outputs=[editor_panel, editor_text, editor_speaker, editor_header, editor_idx_state],
        )

        # Save → apply changes, regenerate views, hide panel
        editor_save_btn.click(
            fn=save_segment_edit,
            inputs=[
                editor_idx_state, editor_text, editor_speaker,
                result_state, names_state,
                client_input, project_input, date_input, export_format,
                search_input, speaker_filter,
            ],
            outputs=[
                transcript_html, transcript_text_box, speaker_table,
                names_state, download_file, result_state, editor_panel,
            ],
        )

        # Cancel → just hide
        editor_cancel_btn.click(fn=close_segment_editor, outputs=[editor_panel])

        # ── Saved voices events ──
        voices_table.select(
            fn=remember_voice_selection,
            inputs=None,
            outputs=[voices_selected_state],
        )
        voices_refresh_btn.click(fn=refresh_voices_table, outputs=[voices_table])
        voices_delete_btn.click(
            fn=delete_voice_profile,
            inputs=[voices_table, voices_selected_state],
            outputs=[voices_table, voices_selected_state],
        )
        # Refresh voices table after each Apply Names (it may have learned new profiles)
        apply_names_btn.click(fn=refresh_voices_table, outputs=[voices_table])
        # Initial population on page load
        app.load(fn=refresh_voices_table, outputs=[voices_table])

        # Attach keyboard shortcuts + segment-click delegation after page load
        app.load(fn=None, inputs=None, outputs=None, js=keyboard_js)

    return app
