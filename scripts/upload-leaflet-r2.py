#!/usr/bin/env python3
"""Upload the generated Leaflet webp tiles to a Cloudflare R2 bucket (S3 API).

R2 is S3-compatible, so this uses boto3 against the R2 endpoint. Tiles are many
small files (~19.5k, ~106 MB), so uploads run in parallel. Objects are written
with Content-Type image/webp and a long immutable Cache-Control (tiles never
change once baked).

Keys mirror the local era/continent layout under R2_PREFIX (default: the era), e.g.
    precata/Northrend/z6x0y0.webp
so the frontend base URL + "/<era>/<Continent>/..." lines up with local. The era
folder is chosen with --era / LEAFLET_ERA (default precata) - matching DataConfig's
ClientEra grouping, so cata tiles never collide with precata.

CREDENTIALS come from env vars ONLY (never pass secrets on the command line):
    R2_ACCOUNT_ID          Cloudflare account id (endpoint host)
    R2_ACCESS_KEY_ID       R2 API token access key id
    R2_SECRET_ACCESS_KEY   R2 API token secret
    R2_BUCKET              target bucket name
  optional:
    R2_ENDPOINT            override endpoint (default https://<acct>.r2.cloudflarestorage.com)
    R2_PREFIX              key prefix (default: the era, e.g. precata)
    LEAFLET_ERA           era folder to upload (default: precata)

REQUIREMENTS  pip install boto3
USAGE   python scripts/upload-leaflet-r2.py                 # upload all (overwrite)
        python scripts/upload-leaflet-r2.py --skip-existing # skip keys already present
        python scripts/upload-leaflet-r2.py --dry-run       # list what would upload
        python scripts/upload-leaflet-r2.py --continent Northrend
        python scripts/upload-leaflet-r2.py --era precata
"""
import os, sys, argparse, threading, concurrent.futures as cf

try:
    import boto3
    from botocore.config import Config
    from botocore.exceptions import ClientError
except ImportError:
    sys.exit("boto3 required: pip install boto3")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LEAFLET_DIR = os.path.join(ROOT, "Json", "leaflet")

CONTENT_TYPE = "image/webp"
CACHE_CONTROL = "public, max-age=31536000, immutable"
WORKERS = 32


def env(name, required=True, default=None):
    v = os.environ.get(name, default)
    if required and not v:
        sys.exit(f"missing env var {name}")
    return v


def make_client():
    account = env("R2_ACCOUNT_ID")
    endpoint = os.environ.get("R2_ENDPOINT", f"https://{account}.r2.cloudflarestorage.com")

    # Accept short names (R2_AC / R2_SAK) as fallbacks for the standard ones.
    access = os.environ.get("R2_ACCESS_KEY_ID") or os.environ.get("R2_AC")
    secret = os.environ.get("R2_SECRET_ACCESS_KEY") or os.environ.get("R2_SAK")
    if not access:
        sys.exit("missing R2_ACCESS_KEY_ID (or R2_AC)")
    if not secret:
        sys.exit("missing R2_SECRET_ACCESS_KEY (or R2_SAK)")

    return boto3.client(
        "s3",
        endpoint_url=endpoint,
        aws_access_key_id=access,
        aws_secret_access_key=secret,
        region_name="auto",
        config=Config(retries={"max_attempts": 5, "mode": "standard"}),
    )


def collect(prefix, continent, src):
    items = []
    for dirpath, _, files in os.walk(src):
        for f in files:
            if not f.endswith(".webp"):
                continue
            full = os.path.join(dirpath, f)
            rel = os.path.relpath(full, src).replace(os.sep, "/")
            if continent and not rel.startswith(continent + "/"):
                continue
            items.append((full, f"{prefix}/{rel}"))
    return items


INDEX_KEY = "leaflet/index.json"


def fetch_index(client, bucket, key):
    """The index describes EVERY era bundle in the bucket, not just this run's, so
    an era-scoped upload must merge rather than replace - publishing only
    `--era mop` over a full index would strip the precata and cata entries and
    break download-leaflet.ps1 for everyone already using them. Same hazard the
    navmesh uploader hit."""
    import json

    try:
        body = client.get_object(Bucket=bucket, Key=key)["Body"].read()
        existing = json.loads(body.decode("utf-8")).get("eras", [])
        print(f"merging into existing {key} ({len(existing)} entries)")
        return existing
    except ClientError:
        print(f"no existing {key}; writing a fresh one")
        return []
    except (ValueError, KeyError) as e:
        sys.exit(f"existing {key} is unreadable ({e}) - refusing to overwrite it blindly")


def write_index(client, bucket, era, zip_key, tiles, size):
    """era -> zip/tiles/bytes, so download-leaflet.ps1 can tell which eras are
    published and whether a local copy is complete instead of guessing."""
    import json

    entries = {x.get("era"): x for x in fetch_index(client, bucket, INDEX_KEY)}
    entries[era] = {"era": era, "zip": zip_key, "tiles": tiles, "bytes": size}

    index = {"eras": [entries[k] for k in sorted(entries, key=lambda k: k or "")]}
    client.put_object(
        Bucket=bucket, Key=INDEX_KEY,
        Body=json.dumps(index, indent=2).encode("utf-8"),
        ContentType="application/json", CacheControl="public, max-age=300",
    )
    print(f"uploaded {INDEX_KEY} ({len(index['eras'])} eras total)")


def build_and_upload_zip(client, bucket, dry_run, src, zip_key):
    """Zip the whole tile set (arcnames <era>/<Continent>/...) and upload it as
    the single offline-download artifact. webp is already compressed, so store
    without re-deflating. Unzip into Json/leaflet/ to get Json/leaflet/<era>/*."""
    import zipfile

    zip_path = os.path.join(ROOT, "Json", "leaflet", zip_key)
    base = os.path.dirname(src)  # Json/leaflet -> arcname <era>/<Continent>/<file>

    n = 0
    print(f"zipping {src} -> {zip_path}")
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_STORED) as z:
        for dirpath, _, files in os.walk(src):
            for f in files:
                if not f.endswith(".webp"):
                    continue
                full = os.path.join(dirpath, f)
                z.write(full, os.path.relpath(full, base).replace(os.sep, "/"))
                n += 1
    size = os.path.getsize(zip_path)
    print(f"  {n} tiles, {size / (1024 * 1024):.0f} MB")

    if dry_run:
        print(f"  (dry-run) would upload -> {zip_key} and update {INDEX_KEY}")
        return

    client.upload_file(zip_path, bucket, zip_key, ExtraArgs={
        "ContentType": "application/zip",
        "CacheControl": "public, max-age=86400",
    })
    print(f"  uploaded -> {zip_key}")

    era = os.path.basename(src.rstrip(os.sep))
    write_index(client, bucket, era, zip_key, n, size)


def count_tiles(src):
    return sum(1 for _, _, files in os.walk(src) for f in files if f.endswith(".webp"))


def publish_index_only(client, bucket, dry_run, only_era):
    """Write leaflet/index.json describing the bundles ALREADY in the bucket.

    Separate from --zip on purpose: the index is a few hundred bytes, and
    regenerating it should not mean re-uploading ~220 MB of unchanged tile zips.
    Tile counts come from the local era dirs, sizes from the objects themselves,
    so the published numbers describe what a downloader will actually receive.
    """
    import json

    eras = [only_era] if only_era else sorted(
        d for d in os.listdir(LEAFLET_DIR) if os.path.isdir(os.path.join(LEAFLET_DIR, d)))

    entries = {} if dry_run else {x.get("era"): x for x in fetch_index(client, bucket, INDEX_KEY)}

    for era in eras:
        src = os.path.join(LEAFLET_DIR, era)
        zip_key = f"{era}-tiles.zip"
        tiles = count_tiles(src)
        if not tiles:
            print(f"  {era}: no local tiles, skipping")
            continue

        if dry_run:
            local = os.path.join(LEAFLET_DIR, zip_key)
            size = os.path.getsize(local) if os.path.isfile(local) else 0
        else:
            try:
                size = client.head_object(Bucket=bucket, Key=zip_key)["ContentLength"]
            except ClientError:
                # No bundle in the bucket -> indexing it would advertise a 404.
                print(f"  {era}: {zip_key} is not in the bucket, skipping "
                      f"(upload it with --era {era} --zip)")
                continue

        entries[era] = {"era": era, "zip": zip_key, "tiles": tiles, "bytes": size}
        print(f"  {era}: {tiles} tiles, {size / (1024 * 1024):.0f} MB -> {zip_key}")

    if not entries:
        sys.exit("nothing to index")

    index = {"eras": [entries[k] for k in sorted(entries, key=lambda k: k or "")]}
    if dry_run:
        print(f"(dry-run) would upload {INDEX_KEY}:")
        print(json.dumps(index, indent=2))
        return

    client.put_object(
        Bucket=bucket, Key=INDEX_KEY,
        Body=json.dumps(index, indent=2).encode("utf-8"),
        ContentType="application/json", CacheControl="public, max-age=300",
    )
    print(f"uploaded {INDEX_KEY} ({len(index['eras'])} eras total)")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--skip-existing", action="store_true", help="skip keys already in the bucket")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--continent", default="", help="only this continent dir (e.g. Northrend)")
    ap.add_argument("--era", default="", help="leaflet era folder (default: LEAFLET_ERA env or precata)")
    ap.add_argument("--zip", action="store_true", help="build + upload the offline .zip instead of per-tile")
    ap.add_argument("--index-only", action="store_true",
                    help="write leaflet/index.json for bundles already in the bucket; uploads no tiles")
    ap.add_argument("--workers", type=int, default=WORKERS)
    args = ap.parse_args()

    if args.index_only:
        client = None if args.dry_run else make_client()
        publish_index_only(client, os.environ.get("R2_BUCKET"), args.dry_run, args.era)
        return

    era = args.era or os.environ.get("LEAFLET_ERA", "precata")
    src = os.path.join(LEAFLET_DIR, era)
    zip_key = f"{era}-tiles.zip"

    if not os.path.isdir(src):
        sys.exit(f"no tiles at {src} - run scripts/extract-minimap.py first (era '{era}')")

    if args.zip:
        client = None if args.dry_run else make_client()
        build_and_upload_zip(client, os.environ.get("R2_BUCKET"), args.dry_run, src, zip_key)
        return

    prefix = (os.environ.get("R2_PREFIX") or era).strip("/")
    items = collect(prefix, args.continent, src)
    print(f"source : {src}")
    print(f"bucket : {os.environ.get('R2_BUCKET', '<R2_BUCKET unset>')}  prefix: {prefix}/  files: {len(items)}")

    if args.dry_run:
        # No credentials needed just to list what would upload.
        for _, key in items[:10]:
            print(f"  would put {key}")
        print(f"  ... ({len(items)} total)")
        return

    bucket = env("R2_BUCKET")
    client = make_client()

    done = [0]
    skipped = [0]
    failed = []
    lock = threading.Lock()

    def exists(key):
        try:
            client.head_object(Bucket=bucket, Key=key)
            return True
        except ClientError:
            return False

    def put(full, key):
        try:
            if args.skip_existing and exists(key):
                with lock:
                    skipped[0] += 1
                return
            client.upload_file(full, bucket, key, ExtraArgs={
                "ContentType": CONTENT_TYPE,
                "CacheControl": CACHE_CONTROL,
            })
        except Exception as e:
            with lock:
                failed.append((key, str(e)))
            return
        with lock:
            done[0] += 1
            n = done[0]
            if n % 500 == 0 or n == len(items):
                print(f"  uploaded {n}/{len(items)} (skipped {skipped[0]})")

    with cf.ThreadPoolExecutor(max_workers=args.workers) as ex:
        list(ex.map(lambda a: put(*a), items))

    print(f"\ndone: uploaded {done[0]}, skipped {skipped[0]}, failed {len(failed)}")
    for key, err in failed[:10]:
        print(f"  FAIL {key}: {err}")
    if failed:
        sys.exit(1)


if __name__ == "__main__":
    main()
