"""把 assets 里的 1080x1080 PNG 转成多尺寸 Windows 图标（纯标准库，PNG 压缩条目）。

用法： python tools/make-icon.py
依赖： ffmpeg（PATH 或 D:\\Awork\\ffmpeg\\bin\\ffmpeg.exe）
"""
import os
import shutil
import struct
import subprocess
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "assets", "icon(请用ffmpeg将它转为ico格式).png")
OUT = os.path.join(ROOT, "assets", "icon.ico")
SIZES = [16, 24, 32, 48, 64, 128, 256]

FFMPEG_CANDIDATES = [
    r"D:\Awork\ffmpeg\bin\ffmpeg.exe",
    shutil.which("ffmpeg") or "",
]


def find_ffmpeg() -> str:
    for path in FFMPEG_CANDIDATES:
        if path and os.path.isfile(path):
            return path
    raise SystemExit("找不到 ffmpeg，请确认 PATH 或 D:\\Awork\\ffmpeg\\bin\\ffmpeg.exe")


def main() -> int:
    ffmpeg = find_ffmpeg()
    if not os.path.isfile(SRC):
        raise SystemExit(f"找不到源图：{SRC}")

    images: list[bytes] = []
    with tempfile.TemporaryDirectory() as tmp:
        for size in SIZES:
            png = os.path.join(tmp, f"icon_{size}.png")
            cmd = [ffmpeg, "-y", "-v", "error", "-i", SRC,
                   "-vf", f"scale={size}:{size}:flags=lanczos", png]
            subprocess.run(cmd, check=True)
            with open(png, "rb") as f:
                images.append(f.read())

    count = len(images)
    header = struct.pack("<HHH", 0, 1, count)
    offset = 6 + 16 * count
    entries = bytearray()
    payload = bytearray()
    for size, data in zip(SIZES, images):
        dim = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
        payload += data

    with open(OUT, "wb") as f:
        f.write(header)
        f.write(entries)
        f.write(payload)

    print(f"已生成 {OUT}（{count} 个尺寸：{', '.join(map(str, SIZES))}）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
