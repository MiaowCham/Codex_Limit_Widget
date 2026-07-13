"""Generate flattened ICO and ICNS compatibility icons from the canonical PNG."""
from pathlib import Path
from PIL import Image

root = Path(__file__).resolve().parent.parent
source = root / "icon.png"
icns_output = root / "CodexLimitWidget.icns"
ico_output = root / "icon.ico"

canvas = Image.open(source).convert("RGBA")

# ICO and ICNS are flattened compatibility resources. The .icon project
# remains the editable layered source for Icon Composer and Liquid Glass.
canvas.save(ico_output, format="ICO", sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)])
canvas.save(icns_output, format="ICNS", sizes=[(16, 16), (32, 32), (64, 64), (128, 128), (256, 256), (512, 512), (1024, 1024)])
print(ico_output)
print(icns_output)
