#!/usr/bin/env python3
"""Extract seamless continent minimaps (Leaflet tile pyramids) from a WoW client.

The client stores minimap art as one BLP per ADT block (map<col>_<row>.blp) under
World\\Minimaps\\<MapDir>\\, md5-renamed and listed in textures\\Minimap\\
md5translate.trs. This stitches the blocks of each continent into the tile
pyramid the PathingAPI/BlazorServer Leaflet map expects:

    Json/leaflet/<era>/<Continent>/z{z}x{x}y{y}.webp

where a native block is 512px, Leaflet tiles are 256px (so each block is a 2x2
group of z6 tiles), y-down, NW origin. Tiles are shared by mesh ERA (precata =
vanilla..wotlk), so one run against a Wrath 3.3.5 client produces the whole
precata set: Azeroth, Kalimdor, Expansion01 (Outland) and Northrend.

The placement is the exact inverse of leaflet-watch.js screenToAdt/worldTolatLng,
so the generated tiles line up with the frontend's world<->pixel transform. For
each continent the script prints a ready-to-paste `Configs` entry (resX, resY,
offset) - the offset/resolution the frontend needs.

LOCAL ONLY. Tiles are large and not committed; regenerate from the client.

REQUIREMENTS  pip install Pillow  (Pillow decodes BLP and writes WEBP natively)
              StormLib_x64.dll (shipped at PPather/MPQ/StormLib_x64.dll)
              A WoW client's data archives (default: the repo's Json/MPQ set)
ENV           WOW_MPQ    dir of client *.MPQ (default <repo>/Json/MPQ)
              STORMLIB   StormLib dll (default <repo>/PPather/MPQ/StormLib_x64.dll)
              LEAFLET_ERA  output era folder (default: precata)
USAGE         python scripts/extract-minimap.py               # all continents found
              python scripts/extract-minimap.py --maps 571    # only Northrend
              python scripts/extract-minimap.py --preview      # also dump flat PNGs
"""
import os, sys, io, json, argparse, glob, ctypes as C

try:
    from PIL import Image
except ImportError:
    sys.exit("Pillow required: pip install Pillow")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MPQ_DIR = os.environ.get("WOW_MPQ", os.path.join(ROOT, "Json", "MPQ"))
STORMLIB = os.environ.get("STORMLIB", os.path.join(ROOT, "PPather", "MPQ", "StormLib_x64.dll"))
ERA = os.environ.get("LEAFLET_ERA", "precata")
OUT_ROOT = os.path.join(ROOT, "Json", "leaflet", ERA)

BLP = 512               # native minimap block size, px (this client generation)
TILE = 256              # Leaflet tile size, px
MAXZOOM = 6             # native zoom; a 512 block == a 2x2 group of z6 tiles
MINZOOM = 2             # frontend minZoom; no point emitting below this
WEBP_Q = 85

# Continent MapDir (as it appears in md5translate) -> MapID. Only these world
# continents are shipped; instance/transport minimap dirs are ignored.
CONTINENTS = {
    "Azeroth": 0,
    "Kalimdor": 1,
    "Expansion01": 530,
    "Northrend": 571,
}

# Load order: later archives override earlier (highest precedence last).
def archive_sort_key(path):
    name = os.path.basename(path).lower()
    order = ["common.mpq", "common-2.mpq", "expansion.mpq", "lichking.mpq"]
    if name in order:
        return (0, order.index(name))
    if name.startswith("patch"):
        return (1, name)          # patches after the base set
    return (2, name)              # locale / anything else last


class Storm:
    def __init__(self, dll_path, mpq_dir):
        if not os.path.exists(dll_path):
            sys.exit(f"StormLib not found: {dll_path}\nSet STORMLIB env var.")
        d = C.WinDLL(dll_path)
        d.SFileOpenArchive.argtypes = [C.c_wchar_p, C.c_uint32, C.c_uint32, C.POINTER(C.c_void_p)]
        d.SFileOpenArchive.restype = C.c_int
        d.SFileOpenFileEx.argtypes = [C.c_void_p, C.c_char_p, C.c_uint32, C.POINTER(C.c_void_p)]
        d.SFileOpenFileEx.restype = C.c_int
        d.SFileGetFileSize.argtypes = [C.c_void_p, C.POINTER(C.c_uint32)]
        d.SFileGetFileSize.restype = C.c_uint32
        d.SFileReadFile.argtypes = [C.c_void_p, C.c_void_p, C.c_uint32, C.POINTER(C.c_uint32), C.c_void_p]
        d.SFileReadFile.restype = C.c_int
        d.SFileCloseFile.argtypes = [C.c_void_p]
        d.SFileCloseArchive.argtypes = [C.c_void_p]
        self.d = d
        self.handles = []
        archives = sorted(
            glob.glob(os.path.join(mpq_dir, "*.MPQ")) + glob.glob(os.path.join(mpq_dir, "*.mpq")),
            key=archive_sort_key,
        )
        for p in archives:
            h = C.c_void_p()
            if d.SFileOpenArchive(p, 0, 0x100, C.byref(h)):
                self.handles.append(h)
                print(f"  opened {os.path.basename(p)}")
        if not self.handles:
            sys.exit(f"No MPQ archives opened from {mpq_dir}\nSet WOW_MPQ env var.")

    def read(self, name):
        b = name.encode("latin1")
        for h in reversed(self.handles):   # highest precedence first
            hf = C.c_void_p()
            if not self.d.SFileOpenFileEx(h, b, 0, C.byref(hf)):
                continue
            sz = self.d.SFileGetFileSize(hf, None)
            if sz in (0, 0xFFFFFFFF):
                self.d.SFileCloseFile(hf)
                continue
            buf = (C.c_char * sz)()
            rd = C.c_uint32()
            self.d.SFileReadFile(hf, buf, sz, C.byref(rd), None)
            self.d.SFileCloseFile(hf)
            return bytes(buf[: rd.value])
        return None

    def close(self):
        for h in self.handles:
            self.d.SFileCloseArchive(h)


def parse_trs(storm):
    """md5translate.trs -> {MapDir: {(col,row): md5name}}. Handles both the
    `dir <name>` header form and the flat `<dir>\\map..blp` path form."""
    raw = storm.read("textures\\Minimap\\md5translate.trs")
    if not raw:
        sys.exit("md5translate.trs not found in the client archives")
    grid = {}
    cur = None
    for ln in raw.decode("latin1", "replace").splitlines():
        low = ln.lower()
        if low.startswith("dir:"):
            cur = ln.split(":", 1)[1].strip().replace("/", "\\").split("\\")[-1]
            continue
        if low.startswith("dir ") or (low.startswith("dir\t")):
            cur = ln[4:].strip()
            continue
        if "\t" not in ln:
            continue
        left, md5 = ln.split("\t", 1)
        parts = left.replace("/", "\\").split("\\")
        fn = parts[-1]
        d = parts[-2] if len(parts) >= 2 else cur
        if d is None or not fn.lower().startswith("map"):
            continue
        try:
            col, row = fn[3:].rsplit(".", 1)[0].split("_")
            col, row = int(col), int(row)
        except ValueError:
            continue
        grid.setdefault(d, {})[(col, row)] = md5.strip()
    return grid


def load_native(storm, tiles):
    """{(col,row): md5} -> {(col,row): PIL.Image RGB(A) BLPxBLP}. md5-cached
    (ocean/blank blocks share one md5 across many positions)."""
    cache, out = {}, {}
    for (col, row), md5 in tiles.items():
        img = cache.get(md5)
        if img is None:
            b = storm.read(f"textures\\Minimap\\{md5}")
            if not b:
                continue
            try:
                img = Image.open(io.BytesIO(b)).convert("RGB")
                if img.size != (BLP, BLP):
                    img = img.resize((BLP, BLP), Image.LANCZOS)
            except Exception as e:
                print(f"  ! decode {md5}: {e}")
                continue
            cache[md5] = img
        out[(col, row)] = img
    return out


def save_tile(img, z, x, y, out_dir):
    if img.mode == "RGBA" and img.getextrema()[3][0] == 255:
        img = img.convert("RGB")
    img.save(os.path.join(out_dir, f"z{z}x{x}y{y}.webp"), "WEBP", quality=WEBP_Q, method=6)


def build_pyramid(native, out_dir):
    """native {(col,row): 512 img} -> z{z}x{x}y{y}.webp pyramid, offset-relative,
    y-down. Returns (offset_min_x=minRow, offset_min_y=minCol, resX, resY, count)."""
    os.makedirs(out_dir, exist_ok=True)
    cols = [c for c, _ in native]
    rows = [r for _, r in native]
    min_col, max_col = min(cols), max(cols)
    min_row, max_row = min(rows), max(rows)
    res_x = (max_col - min_col + 1) * BLP
    res_y = (max_row - min_row + 1) * BLP

    n = 0
    level = {}   # (tx,ty) -> 256 img, at z = MAXZOOM
    step = BLP // TILE   # 2: a 512 block splits into 2x2 256 tiles
    for (col, row), img in native.items():
        base_tx = (col - min_col) * step
        base_ty = (row - min_row) * step
        for sx in range(step):
            for sy in range(step):
                sub = img.crop((sx * TILE, sy * TILE, (sx + 1) * TILE, (sy + 1) * TILE))
                tx, ty = base_tx + sx, base_ty + sy
                save_tile(sub, MAXZOOM, tx, ty, out_dir)
                level[(tx, ty)] = sub
                n += 1

    for z in range(MAXZOOM - 1, MINZOOM - 1, -1):
        groups = {}
        for (tx, ty), img in level.items():
            groups.setdefault((tx // 2, ty // 2), []).append((tx & 1, ty & 1, img))
        nxt = {}
        for (px, py), kids in groups.items():
            canvas = Image.new("RGBA", (TILE * 2, TILE * 2), (0, 0, 0, 0))
            for (qx, qy, img) in kids:
                canvas.paste(img, (qx * TILE, qy * TILE))
            small = canvas.resize((TILE, TILE), Image.LANCZOS)
            save_tile(small, z, px, py, out_dir)
            nxt[(px, py)] = small
            n += 1
        level = nxt

    return min_row, min_col, res_x, res_y, n


def preview(native, path):
    cols = [c for c, _ in native]; rows = [r for _, r in native]
    c0, c1, r0, r1 = min(cols), max(cols), min(rows), max(rows)
    scale = 8
    W, H = (c1 - c0 + 1) * scale, (r1 - r0 + 1) * scale
    canvas = Image.new("RGB", (W, H), (10, 12, 18))
    for (col, row), img in native.items():
        canvas.paste(img.resize((scale, scale), Image.LANCZOS), ((col - c0) * scale, (row - r0) * scale))
    canvas.save(path)
    print(f"  preview -> {os.path.relpath(path, ROOT)} ({W}x{H})")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--maps", default="", help="comma MapIDs (default: all continents present)")
    ap.add_argument("--preview", action="store_true", help="also write a flat preview PNG per continent")
    args = ap.parse_args()
    want = {int(x) for x in args.maps.split(",") if x.strip()}

    print(f"client MPQ : {MPQ_DIR}")
    print(f"output     : {OUT_ROOT}  (era '{ERA}')")
    storm = Storm(STORMLIB, MPQ_DIR)
    grid = parse_trs(storm)

    manifest = {}
    for dir_name, map_id in CONTINENTS.items():
        if want and map_id not in want:
            continue
        tiles = grid.get(dir_name)
        if not tiles:
            print(f"{dir_name} (map {map_id}): no minimap blocks in client, skipping")
            continue
        print(f"{dir_name} (map {map_id}): {len(tiles)} blocks")
        native = load_native(storm, tiles)
        if not native:
            print("  ! no decodable blocks"); continue
        out_dir = os.path.join(OUT_ROOT, dir_name)
        off_x, off_y, res_x, res_y, n = build_pyramid(native, out_dir)
        if args.preview:
            preview(native, os.path.join(OUT_ROOT, f"preview-{dir_name}.png"))
        manifest[dir_name] = {
            "resX": res_x, "resY": res_y, "maxZoom": MAXZOOM,
            "MapID": map_id, "offset": {"min": {"x": off_x, "y": off_y}},
        }
        print(f"  -> {n} tiles  resX={res_x} resY={res_y} offset.min={{x:{off_x},y:{off_y}}}")
    storm.close()

    print("\nFrontend Configs entries (paste under the era block in leaflet-watch.js):")
    for name, cfg in manifest.items():
        o = cfg["offset"]["min"]
        print(f"  '{name}': {{ resX: {cfg['resX']}, resY: {cfg['resY']}, "
              f"maxZoom: {cfg['maxZoom']}, MapID: {cfg['MapID']}, "
              f"offset: {{ min: {{ x: {o['x']}, y: {o['y']} }} }} }},")


if __name__ == "__main__":
    main()
