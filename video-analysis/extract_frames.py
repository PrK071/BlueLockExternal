# Python | extract_frames.py | bundled Python + PyAV/Pillow | offline recording analysis.
import sys
from pathlib import Path

root = Path(__file__).parent
sys.path.insert(0, str(root / 'packages'))
import av
from PIL import Image, ImageDraw

source = Path(r'C:\Users\luiz\Videos\2026-10-18 03-13-06.mp4')
output = root / 'frames'
output.mkdir(exist_ok=True)
images = []
with av.open(str(source)) as recording:
    for second in range(0, 39, 2):
        recording.seek(int(second * av.time_base))
        for frame in recording.decode(video=0):
            if frame.time is not None and frame.time >= second:
                picture = frame.to_image()
                path = output / f'{second:02d}s.png'
                picture.save(path)
                images.append((second, picture))
                break

for start in range(0, len(images), 12):
    page = Image.new('RGB', (1280, 4 * 264), '#151b26')
    draw = ImageDraw.Draw(page)
    for i, (second, picture) in enumerate(images[start:start+12]):
        x, y = (i % 3)*426, (i//3)*264
        picture.thumbnail((426, 240))
        page.paste(picture, (x, y+24))
        draw.text((x+8,y+4), f'{second:02d} seconds', fill='white')
    path = output / f'contact-{start//12+1}.jpg'
    page.save(path, quality=88)
    print(path)
