"""Main GUI application for Meeting Transcription."""

import json
import os
import re
import time
import logging
import threading
import tempfile
from pathlib import Path
from datetime import datetime
from typing import Optional
from queue import Queue

import customtkinter as ctk
from tkinter import filedialog, messagebox
from PIL import Image

from ..audio.extractor import AudioExtractor, SUPPORTED_FORMATS, SUPPORTED_VIDEO_FORMATS, SUPPORTED_AUDIO_FORMATS, is_audio_only
from ..transcription.base import TranscriptionResult, MODEL_SIZES
from ..transcription.whisper_transcriber import WhisperTranscriber
from ..transcription.faster_whisper_transcriber import FasterWhisperTranscriber
from ..transcription.whisperx_transcriber import WhisperXTranscriber
from ..diarization.speaker_diarizer import SpeakerDiarizer
from ..utils.gpu_detector import is_cuda_available, get_device_info, enable_gpu_optimizations

logger = logging.getLogger(__name__)

# Configure appearance
ctk.set_appearance_mode("system")
ctk.set_default_color_theme("blue")

CONFIG_PATH = Path.home() / ".meeting-transcription" / "config.json"

# Pipeline step definitions
PIPELINE_STEPS = [
    ("audio", "Audio"),
    ("model", "Model"),
    ("transcription", "Transcription"),
    ("diarization", "Diarization"),
    ("output", "Output"),
]


class CollapsibleSection(ctk.CTkFrame):
    """A frame with a clickable header that toggles content visibility."""

    def __init__(self, parent, title: str, expanded: bool = True, **kwargs):
        super().__init__(parent, **kwargs)
        self._expanded = expanded
        self._title = title

        # Header button
        self._header_btn = ctk.CTkButton(
            self,
            text=self._header_text(),
            command=self._toggle,
            font=ctk.CTkFont(size=13, weight="bold"),
            fg_color="transparent",
            text_color=("gray10", "gray90"),
            hover_color=("gray80", "gray30"),
            anchor="w",
            height=32,
        )
        self._header_btn.pack(fill="x", padx=5, pady=(5, 0))

        # Content frame
        self.content = ctk.CTkFrame(self, fg_color="transparent")
        if expanded:
            self.content.pack(fill="x", padx=10, pady=(0, 5))

    def _header_text(self) -> str:
        arrow = "\u25BC" if self._expanded else "\u25B6"
        return f"{arrow}  {self._title}"

    def _toggle(self):
        self._expanded = not self._expanded
        self._header_btn.configure(text=self._header_text())
        if self._expanded:
            self.content.pack(fill="x", padx=10, pady=(0, 5))
        else:
            self.content.pack_forget()


class StepProgressBar(ctk.CTkFrame):
    """Visual pipeline progress indicator showing named steps."""

    def __init__(self, parent, steps: list[tuple[str, str]], **kwargs):
        super().__init__(parent, fg_color="transparent", **kwargs)
        self._steps = steps  # [(key, label), ...]
        self._step_labels: dict[str, ctk.CTkLabel] = {}
        self._step_connectors: list[ctk.CTkLabel] = []
        self._current_step: Optional[str] = None

        for i, (key, label) in enumerate(steps):
            if i > 0:
                connector = ctk.CTkLabel(
                    self, text="\u2500\u2500", font=ctk.CTkFont(size=11),
                    text_color="gray50",
                )
                connector.pack(side="left", padx=2)
                self._step_connectors.append(connector)

            step_label = ctk.CTkLabel(
                self,
                text=f"  {label}  ",
                font=ctk.CTkFont(size=11),
                corner_radius=6,
                fg_color=("gray85", "gray25"),
                text_color="gray50",
            )
            step_label.pack(side="left", padx=1)
            self._step_labels[key] = step_label

    def set_active(self, step_key: Optional[str]):
        """Highlight the active step and mark previous steps as done."""
        self._current_step = step_key
        found_active = False
        passed_active = False

        for key, label_text in self._steps:
            lbl = self._step_labels[key]
            if key == step_key:
                # Active step
                lbl.configure(
                    fg_color=("dodgerblue", "#1f6aa5"),
                    text_color="white",
                    font=ctk.CTkFont(size=11, weight="bold"),
                )
                found_active = True
                passed_active = True
            elif not found_active and step_key is not None:
                # Completed step (before active)
                lbl.configure(
                    fg_color=("gray85", "gray25"),
                    text_color=("green", "#2ecc71"),
                    font=ctk.CTkFont(size=11),
                    text=f"  \u2713 {label_text}  ",
                )
            else:
                # Pending step (after active) or reset
                lbl.configure(
                    fg_color=("gray85", "gray25"),
                    text_color="gray50",
                    font=ctk.CTkFont(size=11),
                    text=f"  {label_text}  ",
                )

    def mark_all_done(self):
        """Mark all steps as completed."""
        for key, label_text in self._steps:
            lbl = self._step_labels[key]
            lbl.configure(
                fg_color=("gray85", "gray25"),
                text_color=("green", "#2ecc71"),
                font=ctk.CTkFont(size=11),
                text=f"  \u2713 {label_text}  ",
            )

    def reset(self):
        """Reset all steps to pending state."""
        for key, label_text in self._steps:
            lbl = self._step_labels[key]
            lbl.configure(
                fg_color=("gray85", "gray25"),
                text_color="gray50",
                font=ctk.CTkFont(size=11),
                text=f"  {label_text}  ",
            )


class MeetingTranscriptionApp(ctk.CTk):
    """Main application window."""

    def __init__(self):
        super().__init__()

        enable_gpu_optimizations()

        self.title("Meeting Transcription")
        self.geometry("1000x750")
        self.minsize(800, 550)

        # State
        self._video_path: Optional[str] = None
        self._audio_path: Optional[str] = None
        self._transcription_result: Optional[TranscriptionResult] = None
        self._is_processing = False
        self._cancel_requested = False
        self._progress_queue = Queue()

        # Speaker name mappings (original -> custom name)
        self._speaker_names: dict[str, str] = {}
        self._speaker_entries: dict[str, ctk.CTkEntry] = {}
        self._speaker_images: dict[str, ctk.CTkImage] = {}

        # Meeting metadata
        self._meeting_client: str = ""
        self._meeting_project: str = ""
        self._meeting_date: str = ""

        # Check GPU availability
        self._gpu_available = is_cuda_available()
        device_info = get_device_info()
        logger.info(f"GPU available: {self._gpu_available}, devices: {device_info}")

        # Load saved config
        self._config = self._load_config()

        # Build UI
        self._create_widgets()

        # Apply saved config to widgets
        self._apply_config()

        # Start progress queue processor
        self._process_queue()

    # ── Config persistence ──────────────────────────────────────────

    def _load_config(self) -> dict:
        """Load saved configuration from disk."""
        try:
            if CONFIG_PATH.exists():
                with open(CONFIG_PATH, "r", encoding="utf-8") as f:
                    return json.load(f)
        except Exception as e:
            logger.warning(f"Failed to load config: {e}")
        return {}

    def _save_config(self):
        """Save current configuration to disk."""
        config = {
            "model_size": self.model_var.get(),
            "engine": self.engine_var.get(),
            "diarization": self.diarization_var.get(),
            "language": self.language_entry.get().strip(),
            "condition_on_previous_text": self.condition_prev_var.get(),
            "diar_model": self.diar_model_var.get(),
            "client": self.client_entry.get().strip(),
            "project": self.project_entry.get().strip(),
        }
        # Save HF token only if user provided one
        hf_token = self.hf_token_entry.get().strip()
        if hf_token:
            config["hf_token"] = hf_token

        try:
            CONFIG_PATH.parent.mkdir(parents=True, exist_ok=True)
            with open(CONFIG_PATH, "w", encoding="utf-8") as f:
                json.dump(config, f, indent=2)
            logger.info("Configuration saved")
        except Exception as e:
            logger.warning(f"Failed to save config: {e}")

    def _apply_config(self):
        """Apply loaded configuration to widgets."""
        c = self._config
        if not c:
            return

        if "model_size" in c and c["model_size"] in MODEL_SIZES:
            self.model_var.set(c["model_size"])
        if "engine" in c:
            self.engine_var.set(c["engine"])
        if "diarization" in c:
            self.diarization_var.set(c["diarization"])
        if "language" in c and c["language"]:
            self.language_entry.insert(0, c["language"])
        if "condition_on_previous_text" in c:
            self.condition_prev_var.set(c["condition_on_previous_text"])
        if "diar_model" in c:
            self.diar_model_var.set(c["diar_model"])
        if "hf_token" in c and c["hf_token"]:
            self.hf_token_entry.insert(0, c["hf_token"])
        if "client" in c and c["client"]:
            self.client_entry.insert(0, c["client"])
        if "project" in c and c["project"]:
            self.project_entry.insert(0, c["project"])

    # ── UI creation ─────────────────────────────────────────────────

    def _create_widgets(self):
        """Create all UI widgets with tabbed layout."""
        # Main container
        self.main_frame = ctk.CTkFrame(self)
        self.main_frame.pack(fill="both", expand=True, padx=15, pady=15)

        # === Top: File Selection (compact) ===
        self._create_file_section()

        # === Tabview: Settings | Transcript ===
        self.tabview = ctk.CTkTabview(self.main_frame, height=400)
        self.tabview.pack(fill="both", expand=True, pady=(0, 10))

        self.tabview.add("Settings")
        self.tabview.add("Transcript")

        self._create_settings_tab()
        self._create_transcript_tab()

        # === Bottom: Progress + Controls ===
        self._create_progress_section()
        self._create_control_section()

    def _create_file_section(self):
        """Create compact file selection row."""
        file_frame = ctk.CTkFrame(self.main_frame)
        file_frame.pack(fill="x", pady=(0, 10))

        ctk.CTkLabel(
            file_frame,
            text="Media File:",
            font=ctk.CTkFont(size=13, weight="bold"),
        ).pack(side="left", padx=(10, 5), pady=8)

        self.file_entry = ctk.CTkEntry(
            file_frame,
            placeholder_text="Select a video or audio file...",
        )
        self.file_entry.pack(side="left", fill="x", expand=True, padx=5, pady=8)

        self.browse_btn = ctk.CTkButton(
            file_frame, text="Browse", command=self._browse_file, width=90,
        )
        self.browse_btn.pack(side="right", padx=(5, 10), pady=8)

    # ── Settings tab ────────────────────────────────────────────────

    def _create_settings_tab(self):
        """Create the Settings tab with collapsible sections."""
        tab = self.tabview.tab("Settings")

        # Scrollable container for settings
        settings_scroll = ctk.CTkScrollableFrame(tab, fg_color="transparent")
        settings_scroll.pack(fill="both", expand=True)

        # ── Meeting Info section ──
        meeting_section = CollapsibleSection(
            settings_scroll, "Meeting Information", expanded=True,
        )
        meeting_section.pack(fill="x", pady=(0, 5))
        self._create_metadata_fields(meeting_section.content)

        # ── Engine & Model section ──
        engine_section = CollapsibleSection(
            settings_scroll, "Engine & Model", expanded=True,
        )
        engine_section.pack(fill="x", pady=(0, 5))
        self._create_engine_fields(engine_section.content)

        # ── Advanced section (collapsed by default) ──
        advanced_section = CollapsibleSection(
            settings_scroll, "Advanced Settings", expanded=False,
        )
        advanced_section.pack(fill="x", pady=(0, 5))
        self._create_advanced_fields(advanced_section.content)

    def _create_metadata_fields(self, parent):
        """Create meeting metadata fields inside a collapsible section."""
        grid = ctk.CTkFrame(parent, fg_color="transparent")
        grid.pack(fill="x", pady=5)

        # Row 1: Client + Project
        row1 = ctk.CTkFrame(grid, fg_color="transparent")
        row1.pack(fill="x", pady=2)

        ctk.CTkLabel(row1, text="Client:", font=ctk.CTkFont(size=13), width=70, anchor="e").pack(side="left", padx=(0, 5))
        self.client_entry = ctk.CTkEntry(row1, placeholder_text="Client name", width=180)
        self.client_entry.pack(side="left", padx=(0, 20))

        ctk.CTkLabel(row1, text="Project:", font=ctk.CTkFont(size=13), width=70, anchor="e").pack(side="left", padx=(0, 5))
        self.project_entry = ctk.CTkEntry(row1, placeholder_text="Project name", width=180)
        self.project_entry.pack(side="left")

        # Row 2: Date
        row2 = ctk.CTkFrame(grid, fg_color="transparent")
        row2.pack(fill="x", pady=2)

        ctk.CTkLabel(row2, text="Date:", font=ctk.CTkFont(size=13), width=70, anchor="e").pack(side="left", padx=(0, 5))
        self.date_entry = ctk.CTkEntry(row2, placeholder_text="YYYY-MM-DD", width=140)
        self.date_entry.pack(side="left")

    def _create_engine_fields(self, parent):
        """Create engine/model settings fields."""
        # Row 1: Model + Engine + GPU indicator
        row1 = ctk.CTkFrame(parent, fg_color="transparent")
        row1.pack(fill="x", pady=5)

        ctk.CTkLabel(row1, text="Model:", font=ctk.CTkFont(size=13), width=70, anchor="e").pack(side="left", padx=(0, 5))
        self.model_var = ctk.StringVar(value="base")
        self.model_dropdown = ctk.CTkOptionMenu(
            row1, variable=self.model_var, values=MODEL_SIZES, width=120,
        )
        self.model_dropdown.pack(side="left", padx=(0, 20))

        ctk.CTkLabel(row1, text="Engine:", font=ctk.CTkFont(size=13), width=70, anchor="e").pack(side="left", padx=(0, 5))
        default_engine = "faster-whisper" if self._gpu_available else "Whisper"
        self.engine_var = ctk.StringVar(value=default_engine)
        self.engine_dropdown = ctk.CTkOptionMenu(
            row1, variable=self.engine_var,
            values=["faster-whisper", "WhisperX", "Whisper"], width=140,
        )
        self.engine_dropdown.pack(side="left")

        gpu_text = "GPU" if self._gpu_available else "CPU only"
        gpu_color = "green" if self._gpu_available else "gray"
        ctk.CTkLabel(
            row1, text=f"({gpu_text})", text_color=gpu_color,
            font=ctk.CTkFont(size=11),
        ).pack(side="left", padx=8)

        # Row 2: Language + Diarization toggle
        row2 = ctk.CTkFrame(parent, fg_color="transparent")
        row2.pack(fill="x", pady=5)

        ctk.CTkLabel(row2, text="Language:", font=ctk.CTkFont(size=13), width=70, anchor="e").pack(side="left", padx=(0, 5))
        self.language_entry = ctk.CTkEntry(
            row2, placeholder_text="Auto (e.g. pt, en, es...)", width=180,
        )
        self.language_entry.pack(side="left", padx=(0, 20))

        self.diarization_var = ctk.BooleanVar(value=True)
        self.diarization_checkbox = ctk.CTkCheckBox(
            row2, text="Speaker Diarization", variable=self.diarization_var,
        )
        self.diarization_checkbox.pack(side="left")

    def _create_advanced_fields(self, parent):
        """Create advanced settings fields (collapsed by default)."""
        # Row 1: HF Token
        row1 = ctk.CTkFrame(parent, fg_color="transparent")
        row1.pack(fill="x", pady=5)

        ctk.CTkLabel(row1, text="HF Token:", font=ctk.CTkFont(size=13), width=110, anchor="e").pack(side="left", padx=(0, 5))
        self.hf_token_entry = ctk.CTkEntry(
            row1, placeholder_text="HuggingFace token for diarization",
            width=300, show="*",
        )
        self.hf_token_entry.pack(side="left")

        # Row 2: Condition on previous text + Diarization model
        row2 = ctk.CTkFrame(parent, fg_color="transparent")
        row2.pack(fill="x", pady=5)

        self.condition_prev_var = ctk.BooleanVar(value=False)
        self.condition_prev_checkbox = ctk.CTkCheckBox(
            row2, text="Condition on previous text",
            variable=self.condition_prev_var,
        )
        self.condition_prev_checkbox.pack(side="left", padx=(0, 5))

        ctk.CTkLabel(
            row2, text="(uncheck reduces hallucinations)",
            text_color="gray", font=ctk.CTkFont(size=11),
        ).pack(side="left", padx=(0, 30))

        ctk.CTkLabel(row2, text="Diarization Model:", font=ctk.CTkFont(size=13)).pack(side="left", padx=(0, 5))
        self.diar_model_var = ctk.StringVar(value="community-1")
        self.diar_model_dropdown = ctk.CTkOptionMenu(
            row2, variable=self.diar_model_var,
            values=["community-1", "3.1"], width=130,
        )
        self.diar_model_dropdown.pack(side="left")

    # ── Transcript tab ──────────────────────────────────────────────

    def _create_transcript_tab(self):
        """Create the Transcript tab with output and speaker panel."""
        tab = self.tabview.tab("Transcript")

        # Speaker section (hidden initially, shown after transcription)
        self._create_speaker_section(tab)

        # Output text area
        output_frame = ctk.CTkFrame(tab)
        output_frame.pack(fill="both", expand=True)

        ctk.CTkLabel(
            output_frame, text="Transcript Output:",
            font=ctk.CTkFont(size=14, weight="bold"),
        ).pack(anchor="w", padx=10, pady=(10, 5))

        self.output_text = ctk.CTkTextbox(
            output_frame,
            font=ctk.CTkFont(family="Consolas", size=12),
            wrap="word",
        )
        self.output_text.pack(fill="both", expand=True, padx=10, pady=(0, 10))

    def _create_speaker_section(self, parent):
        """Create speaker management panel (hidden initially)."""
        self.speaker_frame = ctk.CTkFrame(parent)
        # Don't pack yet — shown after transcription with speakers

        self.speaker_header = ctk.CTkLabel(
            self.speaker_frame,
            text="Identified Speakers",
            font=ctk.CTkFont(size=14, weight="bold"),
        )
        self.speaker_header.pack(anchor="w", padx=10, pady=(10, 5))

        # Scrollable container for speaker entries
        self.speaker_entries_frame = ctk.CTkScrollableFrame(
            self.speaker_frame, fg_color="transparent", height=220,
        )
        self.speaker_entries_frame.pack(fill="both", expand=True, padx=10, pady=(0, 10))

    # ── Progress section ────────────────────────────────────────────

    def _create_progress_section(self):
        """Create segmented progress bar with step indicators."""
        progress_frame = ctk.CTkFrame(self.main_frame)
        progress_frame.pack(fill="x", pady=(0, 8))

        # Step indicators row
        step_row = ctk.CTkFrame(progress_frame, fg_color="transparent")
        step_row.pack(fill="x", padx=10, pady=(8, 4))

        self.step_progress = StepProgressBar(step_row, PIPELINE_STEPS)
        self.step_progress.pack(anchor="center")

        # Status text
        self.status_label = ctk.CTkLabel(
            progress_frame, text="Ready", font=ctk.CTkFont(size=12),
        )
        self.status_label.pack(anchor="w", padx=10, pady=(2, 2))

        # Progress bar
        self.progress_bar = ctk.CTkProgressBar(progress_frame, width=400)
        self.progress_bar.pack(fill="x", padx=10, pady=(0, 4))
        self.progress_bar.set(0)

        # Timing display (hidden until processing completes)
        self.timing_frame = ctk.CTkFrame(progress_frame, fg_color="transparent")
        self.timing_label = ctk.CTkLabel(
            self.timing_frame, text="",
            font=ctk.CTkFont(family="Consolas", size=11),
            justify="left", anchor="w",
        )
        self.timing_label.pack(anchor="w", padx=10, pady=(0, 6))

    # ── Control buttons ─────────────────────────────────────────────

    def _create_control_section(self):
        """Create control buttons with copy-to-clipboard."""
        control_frame = ctk.CTkFrame(self.main_frame, fg_color="transparent")
        control_frame.pack(fill="x")

        self.start_btn = ctk.CTkButton(
            control_frame, text="Start Transcription",
            command=self._start_transcription,
            width=160, height=38,
            font=ctk.CTkFont(size=14, weight="bold"),
        )
        self.start_btn.pack(side="left", padx=(0, 8))

        self.cancel_btn = ctk.CTkButton(
            control_frame, text="Cancel",
            command=self._cancel_transcription,
            width=90, height=38, state="disabled", fg_color="gray",
        )
        self.cancel_btn.pack(side="left", padx=(0, 8))

        # Right-aligned buttons
        self.save_btn = ctk.CTkButton(
            control_frame, text="Save", command=self._save_transcript,
            width=90, height=38, state="disabled",
        )
        self.save_btn.pack(side="right", padx=(8, 0))

        self.copy_btn = ctk.CTkButton(
            control_frame, text="Copy", command=self._copy_to_clipboard,
            width=90, height=38, state="disabled",
            fg_color=("gray70", "gray30"),
            hover_color=("gray60", "gray40"),
        )
        self.copy_btn.pack(side="right")

    # ── File browsing ───────────────────────────────────────────────

    def _browse_file(self):
        """Open file dialog to select media file."""
        filetypes = [
            ("Supported media", " ".join(f"*{ext}" for ext in SUPPORTED_FORMATS)),
            ("Video files", " ".join(f"*{ext}" for ext in SUPPORTED_VIDEO_FORMATS)),
            ("Audio files", " ".join(f"*{ext}" for ext in SUPPORTED_AUDIO_FORMATS)),
            ("All files", "*.*"),
        ]
        filepath = filedialog.askopenfilename(filetypes=filetypes)

        if filepath:
            self._video_path = filepath
            self.file_entry.delete(0, "end")
            self.file_entry.insert(0, filepath)
            logger.info(f"Selected file: {filepath}")

            # Try to extract date from filename
            extracted_date = self._extract_date_from_filename(filepath)
            if extracted_date:
                self.date_entry.delete(0, "end")
                self.date_entry.insert(0, extracted_date)
                logger.info(f"Extracted date from filename: {extracted_date}")

    def _extract_date_from_filename(self, filepath: str) -> Optional[str]:
        """Extract date from filename if present. Returns YYYY-MM-DD format."""
        filename = Path(filepath).stem

        patterns = [
            (r'(\d{4})[-_.](\d{2})[-_.](\d{2})', lambda m: f"{m.group(1)}-{m.group(2)}-{m.group(3)}"),
            (r'(\d{2})[-_.](\d{2})[-_.](\d{4})', lambda m: f"{m.group(3)}-{m.group(2)}-{m.group(1)}"),
            (r'(\d{4})(\d{2})(\d{2})', lambda m: f"{m.group(1)}-{m.group(2)}-{m.group(3)}"),
            (r'(\d{1,2})\s*(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\w*\s*(\d{4})',
             lambda m: self._parse_month_date(m.group(2), m.group(1), m.group(3))),
            (r'(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\w*\s*(\d{1,2})\s*,?\s*(\d{4})',
             lambda m: self._parse_month_date(m.group(1), m.group(2), m.group(3))),
        ]

        for pattern, formatter in patterns:
            match = re.search(pattern, filename, re.IGNORECASE)
            if match:
                try:
                    date_str = formatter(match)
                    datetime.strptime(date_str, "%Y-%m-%d")
                    return date_str
                except (ValueError, AttributeError):
                    continue
        return None

    def _parse_month_date(self, month_str: str, day: str, year: str) -> str:
        months = {
            'jan': '01', 'feb': '02', 'mar': '03', 'apr': '04',
            'may': '05', 'jun': '06', 'jul': '07', 'aug': '08',
            'sep': '09', 'oct': '10', 'nov': '11', 'dec': '12',
        }
        month_num = months.get(month_str.lower()[:3], '01')
        return f"{year}-{month_num}-{int(day):02d}"

    # ── Copy to clipboard ───────────────────────────────────────────

    def _copy_to_clipboard(self):
        """Copy transcript text to system clipboard."""
        text = self.output_text.get("1.0", "end").strip()
        if not text:
            return
        self.clipboard_clear()
        self.clipboard_append(text)
        # Brief visual feedback
        original_text = self.copy_btn.cget("text")
        self.copy_btn.configure(text="Copied!")
        self.after(1500, lambda: self.copy_btn.configure(text=original_text))

    # ── Speaker panel ───────────────────────────────────────────────

    def _populate_speaker_panel(self):
        """Populate the speaker panel with detected speakers and stats."""
        for widget in self.speaker_entries_frame.winfo_children():
            widget.destroy()
        self._speaker_entries.clear()
        self._speaker_images.clear()

        if not self._transcription_result:
            return

        speakers = sorted(set(
            seg.speaker for seg in self._transcription_result.segments
            if seg.speaker and seg.speaker != "Unknown"
        ))

        if not speakers:
            self.speaker_frame.pack_forget()
            return

        # Initialize speaker names if not already set
        for speaker in speakers:
            if speaker not in self._speaker_names:
                self._speaker_names[speaker] = speaker

        # Compute speaker statistics
        stats = self._compute_speaker_stats(speakers)

        # Extract frames for each speaker
        speaker_frames = self._extract_speaker_frames(speakers)

        total_speaking_time = sum(s["time"] for s in stats.values()) or 1.0

        for speaker in speakers:
            speaker_row = ctk.CTkFrame(self.speaker_entries_frame, fg_color="transparent")
            speaker_row.pack(fill="x", pady=5)

            # Thumbnail image (if available)
            if speaker in speaker_frames and speaker_frames[speaker]:
                try:
                    pil_image = Image.open(speaker_frames[speaker])
                    pil_image.thumbnail((120, 90), Image.Resampling.LANCZOS)
                    ctk_image = ctk.CTkImage(light_image=pil_image, dark_image=pil_image, size=(120, 90))
                    self._speaker_images[speaker] = ctk_image
                    img_label = ctk.CTkLabel(speaker_row, image=ctk_image, text="")
                    img_label.pack(side="left", padx=(0, 10))
                except Exception as e:
                    logger.warning(f"Failed to load thumbnail for {speaker}: {e}")

            # Speaker info container
            info_frame = ctk.CTkFrame(speaker_row, fg_color="transparent")
            info_frame.pack(side="left", fill="x", expand=True)

            # Original label
            ctk.CTkLabel(
                info_frame, text=f"{speaker}:",
                font=ctk.CTkFont(size=13, weight="bold"),
                width=100, anchor="w",
            ).pack(anchor="w", pady=(0, 3))

            # Name entry row
            name_frame = ctk.CTkFrame(info_frame, fg_color="transparent")
            name_frame.pack(anchor="w")

            ctk.CTkLabel(name_frame, text="Name:", font=ctk.CTkFont(size=12)).pack(side="left", padx=(0, 5))
            entry = ctk.CTkEntry(name_frame, width=200, placeholder_text="Enter name...")
            entry.insert(0, self._speaker_names[speaker])
            entry.pack(side="left")
            entry.bind("<KeyRelease>", lambda e, s=speaker: self._on_speaker_name_change(s))
            entry.bind("<FocusOut>", lambda e, s=speaker: self._on_speaker_name_change(s))
            self._speaker_entries[speaker] = entry

            # Statistics row
            st = stats.get(speaker, {"count": 0, "time": 0.0})
            pct = (st["time"] / total_speaking_time * 100) if total_speaking_time > 0 else 0
            time_str = self._format_duration(st["time"])
            stats_text = f"{st['count']} utterances  \u00b7  {time_str} speaking  \u00b7  {pct:.0f}%"

            stats_label = ctk.CTkLabel(
                info_frame, text=stats_text,
                font=ctk.CTkFont(size=11), text_color="gray",
            )
            stats_label.pack(anchor="w", pady=(3, 0))

            # Mini bar for participation percentage
            bar_frame = ctk.CTkFrame(info_frame, fg_color="transparent", height=6)
            bar_frame.pack(anchor="w", fill="x", pady=(2, 0), padx=(0, 40))

            bar_bg = ctk.CTkFrame(bar_frame, fg_color=("gray80", "gray30"), height=4, corner_radius=2)
            bar_bg.pack(fill="x")

            if pct > 0:
                bar_fill = ctk.CTkFrame(bar_bg, fg_color=("dodgerblue", "#1f6aa5"), height=4, corner_radius=2)
                bar_fill.place(relwidth=max(pct / 100, 0.02), relheight=1.0)

        # Show the speaker frame at top of transcript tab
        self.speaker_frame.pack(fill="x", pady=(0, 10), before=self.speaker_frame.master.winfo_children()[-1])

    def _compute_speaker_stats(self, speakers: list[str]) -> dict[str, dict]:
        """Compute per-speaker statistics: utterance count and total speaking time."""
        stats: dict[str, dict] = {s: {"count": 0, "time": 0.0} for s in speakers}
        if not self._transcription_result:
            return stats
        for seg in self._transcription_result.segments:
            if seg.speaker and seg.speaker in stats:
                stats[seg.speaker]["count"] += 1
                stats[seg.speaker]["time"] += seg.end - seg.start
        return stats

    def _extract_speaker_frames(self, speakers: list[str]) -> dict[str, str]:
        """Extract video frames for each speaker at their first speaking moment."""
        if not self._video_path or not self._transcription_result:
            return {}

        if is_audio_only(self._video_path):
            logger.info("Audio-only file detected, skipping speaker frame extraction")
            return {}

        speaker_frames = {}
        extractor = AudioExtractor()

        for speaker in speakers:
            try:
                first_segment = next(
                    (seg for seg in self._transcription_result.segments if seg.speaker == speaker),
                    None,
                )
                if first_segment:
                    timestamp = (first_segment.start + first_segment.end) / 2
                    frame_path = os.path.join(
                        tempfile.gettempdir(),
                        f"speaker_{speaker.replace(' ', '_')}_{int(timestamp)}.png",
                    )
                    extractor.extract_frame(self._video_path, timestamp, frame_path)
                    speaker_frames[speaker] = frame_path
                    logger.info(f"Extracted frame for {speaker} at {timestamp:.1f}s")
            except Exception as e:
                logger.warning(f"Failed to extract frame for {speaker}: {e}")

        return speaker_frames

    def _on_speaker_name_change(self, original_speaker: str):
        """Handle speaker name change and update transcript."""
        if original_speaker not in self._speaker_entries:
            return
        new_name = self._speaker_entries[original_speaker].get().strip()
        if not new_name:
            new_name = original_speaker
        if self._speaker_names.get(original_speaker) != new_name:
            self._speaker_names[original_speaker] = new_name
            self._refresh_transcript_display()

    def _refresh_transcript_display(self):
        """Refresh the transcript with current speaker names."""
        if not self._transcription_result:
            return
        formatted_text = self._format_transcript_with_names()
        self.output_text.delete("1.0", "end")
        self.output_text.insert("1.0", formatted_text)

    def _format_transcript_with_names(self) -> str:
        """Format transcription with custom speaker names."""
        if not self._transcription_result:
            return ""
        lines = []
        for seg in self._transcription_result.segments:
            timestamp = f"[{self._format_time_static(seg.start)}]"
            if seg.speaker:
                display_name = self._speaker_names.get(seg.speaker, seg.speaker)
                lines.append(f"{timestamp} {display_name}: {seg.text.strip()}")
            else:
                lines.append(f"{timestamp} {seg.text.strip()}")
        return "\n".join(lines)

    @staticmethod
    def _format_time_static(seconds: float) -> str:
        hours = int(seconds // 3600)
        minutes = int((seconds % 3600) // 60)
        secs = int(seconds % 60)
        if hours > 0:
            return f"{hours:02d}:{minutes:02d}:{secs:02d}"
        return f"{minutes:02d}:{secs:02d}"

    # ── Transcription logic ─────────────────────────────────────────

    def _start_transcription(self):
        """Start the transcription process."""
        video_path = self.file_entry.get().strip()
        if not video_path:
            messagebox.showerror("Error", "Please select a media file.")
            return
        if not os.path.exists(video_path):
            messagebox.showerror("Error", "Selected file does not exist.")
            return

        self._video_path = video_path
        self._is_processing = True
        self._cancel_requested = False

        # Reset speaker state
        self._speaker_names.clear()
        self._speaker_entries.clear()
        self._speaker_images.clear()
        self.speaker_frame.pack_forget()

        # Save config before starting
        self._save_config()

        # Update UI state
        self._set_processing_state(True)
        self.output_text.delete("1.0", "end")
        self.step_progress.reset()

        # Switch to Transcript tab to show progress
        self.tabview.set("Transcript")

        # Hide timing from previous run
        self.timing_frame.pack_forget()

        # Get settings
        model_size = self.model_var.get()
        engine = self.engine_var.get()
        use_diarization = self.diarization_var.get()
        hf_token = self.hf_token_entry.get().strip() or None
        language = self.language_entry.get().strip() or None
        condition_on_previous_text = self.condition_prev_var.get()
        diar_model = self.diar_model_var.get()

        thread = threading.Thread(
            target=self._transcription_worker,
            args=(video_path, model_size, engine, use_diarization, hf_token,
                  language, condition_on_previous_text, diar_model),
            daemon=True,
        )
        thread.start()

    def _transcription_worker(
        self,
        video_path: str,
        model_size: str,
        engine: str,
        use_diarization: bool,
        hf_token: Optional[str],
        language: Optional[str] = None,
        condition_on_previous_text: bool = False,
        diar_model: str = "community-1",
    ):
        """Background worker for transcription."""
        timings: dict[str, float] = {}

        try:
            # Step 1: Extract audio
            self._set_pipeline_step("audio")
            status_msg = "Normalizing audio..." if is_audio_only(video_path) else "Extracting audio from video..."
            self._update_progress(0.0, status_msg)
            t0 = time.time()

            extractor = AudioExtractor()
            self._audio_path = extractor.extract(
                video_path,
                progress_callback=lambda p, s: self._update_progress(p * 0.2, s),
            )
            timings["Audio extraction"] = time.time() - t0

            if self._cancel_requested:
                return

            # Step 2: Load transcription model
            self._set_pipeline_step("model")
            self._update_progress(0.2, "Loading transcription model...")

            if engine == "faster-whisper":
                transcriber = FasterWhisperTranscriber(model_size=model_size)
            elif engine == "WhisperX":
                transcriber = WhisperXTranscriber(model_size=model_size)
            else:
                transcriber = WhisperTranscriber(model_size=model_size)

            transcriber.load_model(
                progress_callback=lambda p, s: self._update_progress(0.2 + p * 0.1, s),
            )

            if self._cancel_requested:
                return

            # Step 3: Transcribe
            self._set_pipeline_step("transcription")
            self._update_progress(0.3, f"Transcribing audio ({engine})...")
            t0 = time.time()

            result = transcriber.transcribe(
                self._audio_path,
                language=language,
                condition_on_previous_text=condition_on_previous_text,
                progress_callback=lambda p, s: self._update_progress(0.3 + p * 0.4, s),
            )
            timings["Transcription"] = time.time() - t0

            if self._cancel_requested:
                return

            # Step 4: Speaker diarization (optional)
            if use_diarization:
                self._set_pipeline_step("diarization")
                self._update_progress(0.7, "Analyzing speakers...")
                t0 = time.time()

                try:
                    diarizer = SpeakerDiarizer(hf_token=hf_token, model=diar_model)
                    diarizer.load_model(
                        progress_callback=lambda p, s: self._update_progress(0.7 + p * 0.1, s),
                    )
                    if self._cancel_requested:
                        return

                    diar_segments = diarizer.diarize(
                        self._audio_path,
                        progress_callback=lambda p, s: self._update_progress(0.8 + p * 0.15, s),
                    )
                    result = diarizer.assign_speakers(result, diar_segments)
                    timings["Diarization"] = time.time() - t0
                except Exception as e:
                    logger.warning(f"Diarization failed, continuing without speakers: {e}")
                    self._update_progress(0.95, "Diarization failed, continuing without speakers...")

            # Step 5: Format output
            self._set_pipeline_step("output")
            self._update_progress(0.95, "Formatting output...")

            self._transcription_result = result
            formatted_text = result.to_formatted_text(include_speakers=use_diarization)

            self._update_progress(1.0, "Transcription complete!")
            self._display_result(formatted_text)
            self._send_timings(timings)
            self._progress_queue.put(("steps_done",))

        except Exception as e:
            logger.error(f"Transcription failed: {e}")
            self._show_error(str(e))

        finally:
            self._is_processing = False
            self._finish_processing()

    def _set_pipeline_step(self, step_key: str):
        """Thread-safe pipeline step update."""
        self._progress_queue.put(("step", step_key))

    def _update_progress(self, progress: float, status: str):
        self._progress_queue.put(("progress", progress, status))

    def _display_result(self, text: str):
        self._progress_queue.put(("result", text))

    def _show_error(self, message: str):
        self._progress_queue.put(("error", message))

    def _send_timings(self, timings: dict):
        self._progress_queue.put(("timing", timings))

    def _finish_processing(self):
        self._progress_queue.put(("finish",))

    def _process_queue(self):
        """Process queued UI updates."""
        try:
            while not self._progress_queue.empty():
                item = self._progress_queue.get_nowait()

                if item[0] == "progress":
                    _, progress, status = item
                    self.progress_bar.set(progress)
                    self.status_label.configure(text=status)

                elif item[0] == "step":
                    self.step_progress.set_active(item[1])

                elif item[0] == "steps_done":
                    self.step_progress.mark_all_done()

                elif item[0] == "result":
                    self.output_text.delete("1.0", "end")
                    self.output_text.insert("1.0", item[1])
                    self.save_btn.configure(state="normal")
                    self.copy_btn.configure(state="normal")
                    if self.diarization_var.get():
                        self._populate_speaker_panel()

                elif item[0] == "timing":
                    timings = item[1]
                    total = sum(timings.values())
                    lines = [f"  {'Step':<22} {'Time':>8}"]
                    lines.append("  " + "\u2500" * 32)
                    for step, secs in timings.items():
                        lines.append(f"  {step:<22} {secs:>7.1f}s")
                    lines.append("  " + "\u2500" * 32)
                    lines.append(f"  {'Total':<22} {total:>7.1f}s")
                    self.timing_label.configure(text="\n".join(lines))
                    self.timing_frame.pack(fill="x", after=self.progress_bar)

                elif item[0] == "error":
                    messagebox.showerror("Error", item[1])

                elif item[0] == "finish":
                    self._set_processing_state(False)

        except Exception as e:
            logger.error(f"Queue processing error: {e}")

        self.after(100, self._process_queue)

    def _set_processing_state(self, processing: bool):
        """Update UI state based on processing status."""
        if processing:
            self.start_btn.configure(state="disabled")
            self.cancel_btn.configure(state="normal", fg_color=["#DC3545", "#B02A37"])
            self.browse_btn.configure(state="disabled")
            self.model_dropdown.configure(state="disabled")
            self.engine_dropdown.configure(state="disabled")
            self.diarization_checkbox.configure(state="disabled")
            self.language_entry.configure(state="disabled")
            self.condition_prev_checkbox.configure(state="disabled")
            self.diar_model_dropdown.configure(state="disabled")
            self.save_btn.configure(state="disabled")
            self.copy_btn.configure(state="disabled")
        else:
            self.start_btn.configure(state="normal")
            self.cancel_btn.configure(state="disabled", fg_color="gray")
            self.browse_btn.configure(state="normal")
            self.model_dropdown.configure(state="normal")
            self.engine_dropdown.configure(state="normal")
            self.diarization_checkbox.configure(state="normal")
            self.language_entry.configure(state="normal")
            self.condition_prev_checkbox.configure(state="normal")
            self.diar_model_dropdown.configure(state="normal")
            if self._transcription_result:
                self.save_btn.configure(state="normal")
                self.copy_btn.configure(state="normal")

    def _cancel_transcription(self):
        """Cancel ongoing transcription."""
        self._cancel_requested = True
        self.status_label.configure(text="Cancelling...")
        logger.info("Cancellation requested")

    # ── Save transcript ─────────────────────────────────────────────

    def _save_transcript(self):
        """Save transcript to file."""
        if not self._transcription_result:
            return

        client = self.client_entry.get().strip()
        project = self.project_entry.get().strip()
        meeting_date = self.date_entry.get().strip()

        default_filename = self._generate_default_filename(client, project, meeting_date)

        initial_dir = None
        if self._video_path and os.path.exists(os.path.dirname(self._video_path)):
            initial_dir = os.path.dirname(self._video_path)

        filepath = filedialog.asksaveasfilename(
            defaultextension=".txt",
            filetypes=[("Text files", "*.txt"), ("All files", "*.*")],
            initialfile=default_filename,
            initialdir=initial_dir,
        )

        if filepath:
            try:
                header_lines = ["Meeting Transcription", "=" * 40]
                if client:
                    header_lines.append(f"Client: {client}")
                if project:
                    header_lines.append(f"Project: {project}")
                if meeting_date:
                    header_lines.append(f"Date: {meeting_date}")
                if self._transcription_result.duration:
                    duration_str = self._format_duration(self._transcription_result.duration)
                    header_lines.append(f"Duration: {duration_str}")
                if self._transcription_result.language:
                    header_lines.append(f"Language: {self._transcription_result.language}")

                if self.diarization_var.get():
                    speakers = set(seg.speaker for seg in self._transcription_result.segments if seg.speaker)
                    if speakers:
                        header_lines.append(f"Speakers: {len(speakers)}")
                        for original in sorted(speakers):
                            display_name = self._speaker_names.get(original, original)
                            if display_name != original:
                                header_lines.append(f"  - {original} \u2192 {display_name}")
                            else:
                                header_lines.append(f"  - {display_name}")

                header_lines.extend(["", "=" * 40, ""])
                header = "\n".join(header_lines)

                formatted = (
                    self._format_transcript_with_names()
                    if self.diarization_var.get()
                    else self._transcription_result.to_formatted_text(include_speakers=False)
                )

                with open(filepath, 'w', encoding='utf-8') as f:
                    f.write(header + formatted)

                messagebox.showinfo("Success", f"Transcript saved to:\n{filepath}")
                logger.info(f"Transcript saved: {filepath}")
            except Exception as e:
                messagebox.showerror("Error", f"Failed to save: {e}")
                logger.error(f"Failed to save transcript: {e}")

    def _generate_default_filename(self, client: str, project: str, meeting_date: str) -> str:
        parts = []
        if client:
            parts.append(client.replace(" ", "_"))
        if project:
            parts.append(project.replace(" ", "_"))
        if meeting_date:
            parts.append(meeting_date)
        if parts:
            return "_".join(parts) + "_transcript.txt"
        return "transcript.txt"

    def _format_duration(self, seconds: float) -> str:
        hours = int(seconds // 3600)
        minutes = int((seconds % 3600) // 60)
        secs = int(seconds % 60)
        if hours > 0:
            return f"{hours:02d}:{minutes:02d}:{secs:02d}"
        return f"{minutes:02d}:{secs:02d}"


def run_app():
    """Launch the application."""
    log_format = '%(asctime)s - %(name)s - %(levelname)s - %(message)s'
    logging.basicConfig(
        level=logging.INFO,
        format=log_format,
        handlers=[
            logging.StreamHandler(),
            logging.FileHandler('transcription.log', mode='w'),
        ],
    )
    app = MeetingTranscriptionApp()
    app.mainloop()
