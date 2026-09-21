# MIT License
#
# Copyright (c) 2020- CNRS
#
# Permission is hereby granted, free of charge, to any person obtaining a copy
# of this software and associated documentation files (the "Software"), to deal
# in the Software without restriction, including without limitation the rights
# to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
# copies of the Software, and to permit persons to whom the Software is
# furnished to do so, subject to the following conditions:
#
# The above copyright notice and this permission notice shall be included in all
# copies or substantial portions of the Software.
#
# THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
# IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
# FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
# AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
# LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
# OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
# SOFTWARE.

"""A agregação de janelas do pyannote, e só ela.

`Inference.trim` e `Inference.aggregate` são os dois métodos estáticos de
`pyannote/audio/core/inference.py` (4.0.7, MIT). O arquivo inteiro tem 19
menções a torch; **estes dois não têm nenhuma** — o torch dele está todo na
maquinaria de inferência, que o `Segmentador` deste pacote substitui. Copiá-los
é a única forma de usá-los sem importar o pacote que os contém, porque
`pyannote/audio/__init__.py` importa `core.model`, e ele importa torch.

Copiados **sem alteração de matemática**: saíram do `@staticmethod` para função
de módulo, e nada mais. É disso que depende a régua `V1` — uma linha diferente
aqui desloca meio quadro na linha do tempo, e meio quadro já é fala atribuída à
pessoa errada.
"""

import warnings
from typing import Tuple

import numpy as np
from pyannote.core import SlidingWindow, SlidingWindowFeature


def aggregate(
    scores: SlidingWindowFeature,
    frames: SlidingWindow,
    warm_up: Tuple[float, float] = (0.0, 0.0),
    epsilon: float = 1e-12,
    hamming: bool = False,
    missing: float = np.nan,
    skip_average: bool = False,
) -> SlidingWindowFeature:
    """Aggregation

    Parameters
    ----------
    scores : SlidingWindowFeature
        Raw (unaggregated) scores. Shape is (num_chunks, num_frames_per_chunk, num_classes).
    frames : SlidingWindow
        Frames resolution.
    warm_up : (float, float) tuple, optional
        Left/right warm up duration (in seconds).
    missing : float, optional
        Value used to replace missing (ie all NaNs) values.
    skip_average : bool, optional
        Skip final averaging step.

    Returns
    -------
    aggregated_scores : SlidingWindowFeature
        Aggregated scores. Shape is (num_frames, num_classes)
    """

    num_chunks, num_frames_per_chunk, num_classes = scores.data.shape

    chunks = scores.sliding_window
    frames = SlidingWindow(
        start=chunks.start,
        duration=frames.duration,
        step=frames.step,
    )

    # Hamming window used for overlap-add aggregation
    hamming_window = (
        np.hamming(num_frames_per_chunk).reshape(-1, 1)
        if hamming
        else np.ones((num_frames_per_chunk, 1))
    )

    # anything before warm_up_left (and after num_frames_per_chunk - warm_up_right)
    # will not be used in the final aggregation

    # warm-up windows used for overlap-add aggregation
    warm_up_window = np.ones((num_frames_per_chunk, 1))
    # anything before warm_up_left will not contribute to aggregation
    warm_up_left = round(
        warm_up[0] / scores.sliding_window.duration * num_frames_per_chunk
    )
    warm_up_window[:warm_up_left] = epsilon
    # anything after num_frames_per_chunk - warm_up_right either
    warm_up_right = round(
        warm_up[1] / scores.sliding_window.duration * num_frames_per_chunk
    )
    warm_up_window[num_frames_per_chunk - warm_up_right :] = epsilon

    # aggregated_output[i] will be used to store the sum of all predictions
    # for frame #i
    num_frames = (
        frames.closest_frame(
            scores.sliding_window.start
            + scores.sliding_window.duration
            + (num_chunks - 1) * scores.sliding_window.step
            + 0.5 * frames.duration
        )
        + 1
    )
    aggregated_output: np.ndarray = np.zeros(
        (num_frames, num_classes), dtype=np.float32
    )

    # overlapping_chunk_count[i] will be used to store the number of chunks
    # that contributed to frame #i
    overlapping_chunk_count: np.ndarray = np.zeros(
        (num_frames, num_classes), dtype=np.float32
    )

    # aggregated_mask[i] will be used to indicate whether
    # at least one non-NAN frame contributed to frame #i
    aggregated_mask: np.ndarray = np.zeros((num_frames, num_classes), dtype=np.float32)

    # loop on the scores of sliding chunks
    for chunk, score in scores:
        # chunk ~ Segment
        # score ~ (num_frames_per_chunk, num_classes)-shaped np.ndarray
        # mask ~ (num_frames_per_chunk, num_classes)-shaped np.ndarray
        mask = 1 - np.isnan(score)
        np.nan_to_num(score, copy=False, nan=0.0)

        start_frame = frames.closest_frame(chunk.start + 0.5 * frames.duration)

        aggregated_output[start_frame : start_frame + num_frames_per_chunk] += (
            score * mask * hamming_window * warm_up_window
        )

        overlapping_chunk_count[start_frame : start_frame + num_frames_per_chunk] += (
            mask * hamming_window * warm_up_window
        )

        aggregated_mask[start_frame : start_frame + num_frames_per_chunk] = np.maximum(
            aggregated_mask[start_frame : start_frame + num_frames_per_chunk],
            mask,
        )

    if skip_average:
        average = aggregated_output
    else:
        average = aggregated_output / np.maximum(overlapping_chunk_count, epsilon)

    average[aggregated_mask == 0.0] = missing

    return SlidingWindowFeature(average, frames)


def trim(
    scores: SlidingWindowFeature,
    warm_up: Tuple[float, float] = (0.1, 0.1),
) -> SlidingWindowFeature:
    """Trim left and right warm-up regions

    Parameters
    ----------
    scores : SlidingWindowFeature
        (num_chunks, num_frames, num_classes)-shaped scores.
    warm_up : (float, float) tuple
        Left/right warm up ratio of chunk duration.
        Defaults to (0.1, 0.1), i.e. 10% on both sides.

    Returns
    -------
    trimmed : SlidingWindowFeature
        (num_chunks, trimmed_num_frames, num_speakers)-shaped scores
    """

    assert scores.data.ndim == 3, (
        "Inference.trim expects (num_chunks, num_frames, num_classes)-shaped `scores`"
    )
    _, num_frames, _ = scores.data.shape

    chunks = scores.sliding_window

    num_frames_left = round(num_frames * warm_up[0])
    num_frames_right = round(num_frames * warm_up[1])

    num_frames_step = round(num_frames * chunks.step / chunks.duration)
    if num_frames - num_frames_left - num_frames_right < num_frames_step:
        warnings.warn(
            f"Total `warm_up` is so large ({sum(warm_up) * 100:g}% of each chunk) "
            f"that resulting trimmed scores does not cover a whole step ({chunks.step:g}s)"
        )
    new_data = scores.data[:, num_frames_left : num_frames - num_frames_right]

    new_chunks = SlidingWindow(
        start=chunks.start + warm_up[0] * chunks.duration,
        step=chunks.step,
        duration=(1 - warm_up[0] - warm_up[1]) * chunks.duration,
    )

    return SlidingWindowFeature(new_data, new_chunks)
