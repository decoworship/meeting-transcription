"""Audio extraction and normalization from media files using FFmpeg."""

import subprocess
import tempfile
import os
import logging
from pathlib import Path
from typing import Optional, Callable

logger = logging.getLogger(__name__)

# Supported formats
SUPPORTED_VIDEO_FORMATS = {'.mp4', '.mkv', '.avi', '.mov', '.webm', '.m4v', '.flv', '.wmv'}
SUPPORTED_AUDIO_FORMATS = {'.mp3', '.wav', '.flac', '.ogg', '.m4a', '.aac', '.wma', '.opus'}
SUPPORTED_FORMATS = SUPPORTED_VIDEO_FORMATS | SUPPORTED_AUDIO_FORMATS


def is_audio_only(filepath: str) -> bool:
    """Check if a file is an audio-only format (not video)."""
    return Path(filepath).suffix.lower() in SUPPORTED_AUDIO_FORMATS


class AudioExtractor:
    """Extract and normalize audio from media files using FFmpeg."""

    def __init__(self):
        self._check_ffmpeg()

    def _check_ffmpeg(self) -> None:
        """Verify FFmpeg is installed and accessible."""
        try:
            result = subprocess.run(
                ['ffmpeg', '-version'],
                capture_output=True,
                text=True,
                creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
            )
            if result.returncode != 0:
                raise RuntimeError("FFmpeg check failed")
            logger.info("FFmpeg found and accessible")
        except FileNotFoundError:
            raise RuntimeError(
                "FFmpeg not found. Please install FFmpeg and add it to your PATH."
            )

    def extract(
        self,
        video_path: str,
        output_path: Optional[str] = None,
        progress_callback: Optional[Callable[[float, str], None]] = None
    ) -> str:
        """
        Extract and normalize audio from a media file (video or audio).

        Args:
            video_path: Path to the input media file (video or audio)
            output_path: Optional output path for WAV file. If None, uses temp file.
            progress_callback: Optional callback(progress: float, status: str)

        Returns:
            Path to extracted audio file (WAV format, 16kHz, mono)
        """
        video_path = Path(video_path)

        if not video_path.exists():
            raise FileNotFoundError(f"Media file not found: {video_path}")

        if video_path.suffix.lower() not in SUPPORTED_FORMATS:
            raise ValueError(
                f"Unsupported format: {video_path.suffix}. "
                f"Supported: {', '.join(SUPPORTED_FORMATS)}"
            )

        if output_path is None:
            # Create temp file for extracted audio
            temp_dir = tempfile.mkdtemp(prefix="meeting_transcription_")
            output_path = os.path.join(temp_dir, "audio.wav")

        output_path = Path(output_path)
        output_path.parent.mkdir(parents=True, exist_ok=True)

        if progress_callback:
            is_audio = video_path.suffix.lower() in SUPPORTED_AUDIO_FORMATS
            progress_callback(0.0, "Normalizing audio..." if is_audio else "Extracting audio from video...")

        # Get video duration for progress tracking
        duration = self._get_duration(video_path)

        # FFmpeg command for extraction
        # -vn: no video
        # -acodec pcm_s16le: 16-bit PCM
        # -ar 16000: 16kHz sample rate (optimal for Whisper)
        # -ac 1: mono channel
        cmd = [
            'ffmpeg',
            '-i', str(video_path),
            '-vn',
            '-acodec', 'pcm_s16le',
            '-ar', '16000',
            '-ac', '1',
            '-y',  # Overwrite output
            str(output_path)
        ]

        logger.info(f"Extracting audio: {video_path} -> {output_path}")

        try:
            # Run FFmpeg with progress monitoring
            process = subprocess.Popen(
                cmd,
                stderr=subprocess.PIPE,
                stdout=subprocess.PIPE,
                text=True,
                creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
            )

            # Monitor progress from stderr
            while True:
                line = process.stderr.readline()
                if not line and process.poll() is not None:
                    break

                if progress_callback and duration and 'time=' in line:
                    # Parse time from FFmpeg output
                    try:
                        time_str = line.split('time=')[1].split()[0]
                        current_time = self._parse_time(time_str)
                        progress = min(current_time / duration, 1.0)
                        label = "Normalizing audio" if is_audio else "Extracting audio"
                        progress_callback(progress, f"{label}: {progress*100:.1f}%")
                    except (IndexError, ValueError):
                        pass

            if process.returncode != 0:
                raise RuntimeError(f"FFmpeg extraction failed with code {process.returncode}")

            if progress_callback:
                progress_callback(1.0, "Audio extraction complete")

            logger.info(f"Audio extracted successfully: {output_path}")
            return str(output_path)

        except Exception as e:
            logger.error(f"Audio extraction failed: {e}")
            raise RuntimeError(f"Failed to extract audio: {e}")

    def _get_duration(self, video_path: Path) -> Optional[float]:
        """Get media file duration in seconds using ffprobe."""
        try:
            cmd = [
                'ffprobe',
                '-v', 'error',
                '-show_entries', 'format=duration',
                '-of', 'default=noprint_wrappers=1:nokey=1',
                str(video_path)
            ]
            result = subprocess.run(
                cmd,
                capture_output=True,
                text=True,
                creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
            )
            return float(result.stdout.strip())
        except Exception as e:
            logger.warning(f"Could not get media duration: {e}")
            return None

    def _parse_time(self, time_str: str) -> float:
        """Parse FFmpeg time string (HH:MM:SS.ms) to seconds."""
        parts = time_str.split(':')
        if len(parts) == 3:
            hours, minutes, seconds = parts
            return int(hours) * 3600 + int(minutes) * 60 + float(seconds)
        return 0.0

    def extract_frame(
        self,
        video_path: str,
        timestamp: float,
        output_path: str
    ) -> str:
        """
        Extract a single frame from video at specified timestamp.

        Args:
            video_path: Path to the video file
            timestamp: Time in seconds to extract frame
            output_path: Path to save the frame image (PNG)

        Returns:
            Path to extracted frame
        """
        video_path = Path(video_path)
        output_path = Path(output_path)
        output_path.parent.mkdir(parents=True, exist_ok=True)

        # FFmpeg command to extract single frame
        # -ss: seek to timestamp
        # -vframes 1: extract only 1 frame
        # -q:v 2: high quality (scale 2-31, lower is better)
        cmd = [
            'ffmpeg',
            '-ss', str(timestamp),
            '-i', str(video_path),
            '-vframes', '1',
            '-q:v', '2',
            '-y',  # Overwrite output
            str(output_path)
        ]

        logger.info(f"Extracting frame at {timestamp}s from {video_path}")

        try:
            result = subprocess.run(
                cmd,
                capture_output=True,
                text=True,
                creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
            )

            if result.returncode != 0:
                raise RuntimeError(f"FFmpeg frame extraction failed: {result.stderr}")

            logger.info(f"Frame extracted: {output_path}")
            return str(output_path)

        except Exception as e:
            logger.error(f"Frame extraction failed: {e}")
            raise RuntimeError(f"Failed to extract frame: {e}")


def get_supported_formats() -> set:
    """Return set of all supported media file extensions."""
    return SUPPORTED_FORMATS.copy()
