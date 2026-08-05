from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image


def runs(active: np.ndarray, maximum_gap: int) -> list[tuple[int, int]]:
    indexes = np.flatnonzero(active)
    if indexes.size == 0:
        return []
    found: list[tuple[int, int]] = []
    start = previous = int(indexes[0])
    for raw_index in indexes[1:]:
        index = int(raw_index)
        if index - previous > maximum_gap + 1:
            found.append((start, previous + 1))
            start = index
        previous = index
    found.append((start, previous + 1))
    return found


def normalize_sheet(source_path: Path, output_path: Path) -> None:
    source = Image.open(source_path).convert("RGBA")
    alpha = np.asarray(source.getchannel("A"))
    foreground = alpha > 24
    row_runs = runs(foreground.sum(axis=1) >= 24, maximum_gap=2)
    if len(row_runs) != 3:
        raise RuntimeError(f"Expected 3 animation rows in {source_path.name}, found {len(row_runs)}: {row_runs}")

    regions: list[tuple[int, int, int, int]] = []
    for top, bottom in row_runs:
        row_mask = foreground[top:bottom, :]
        column_runs = runs(row_mask.sum(axis=0) >= 5, maximum_gap=32)
        if len(column_runs) != 4:
            raise RuntimeError(
                f"Expected 4 frames in row {top}:{bottom} of {source_path.name}, "
                f"found {len(column_runs)}: {column_runs}"
            )
        for left, right in column_runs:
            regions.append((max(0, left - 6), max(0, top - 6), min(source.width, right + 6), min(source.height, bottom + 6)))

    if source_path.stem == "artemis-idle":
        regions = [regions[index] for index in (0, 1, 2, 3, 4, 5, 6, 7, 6, 5, 2, 0)]
    elif source_path.stem == "artemis-wave":
        regions = [regions[index] for index in (0, 1, 2, 3, 3, 2, 3, 2, 3, 2, 1, 0)]

    cell_width = 420
    cell_height = 340
    maximum_width = max(right - left for left, _, right, _ in regions)
    maximum_height = max(bottom - top for _, top, _, bottom in regions)
    scale = min((cell_width - 16) / maximum_width, (cell_height - 16) / maximum_height, 1.0)
    atlas = Image.new("RGBA", (cell_width * 4, cell_height * 3), (0, 0, 0, 0))

    for index, (left, top, right, bottom) in enumerate(regions):
        frame = source.crop((left, top, right, bottom))
        if scale < 1:
            frame = frame.resize(
                (max(1, round(frame.width * scale)), max(1, round(frame.height * scale))),
                Image.Resampling.LANCZOS,
            )
        column = index % 4
        row = index // 4
        x = column * cell_width + (cell_width - frame.width) // 2
        y = row * cell_height + cell_height - frame.height - 8
        atlas.alpha_composite(frame, (x, y))

    atlas.save(output_path, optimize=True)
    print(f"{source_path.name}: rows={row_runs}, scale={scale:.4f}, output={output_path.name}")


def main() -> None:
    parser = argparse.ArgumentParser(description="Rebuild generated Artemis sheets into strict isolated sprite cells.")
    parser.add_argument("folder", type=Path)
    args = parser.parse_args()
    for state in ("idle", "run", "release", "wave"):
        normalize_sheet(
            args.folder / f"artemis-{state}.png",
            args.folder / f"artemis-{state}-atlas.png",
        )


if __name__ == "__main__":
    main()
