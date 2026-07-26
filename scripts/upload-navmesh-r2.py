#!/usr/bin/env python3
"""Upload baked navmesh tile bundles to Cloudflare R2 (one zip per era+continent+hash).

Navmesh .dnm tiles are read server-side by PPather in the pathing hot path, so
(unlike leaflet) they are NOT fetched per-tile from R2. Instead each
<era>/<continent>/<settings-hash>/ dir is zipped and hosted; users download + extract
with scripts/download-navmesh.ps1 (runtime unchanged, reads local disk). Layout is
era-partitioned (precata/cata/...) to match DataConfig.Navmesh so pre-cata (MPQ) and
cata (CASC) sets never collide. A bundle only matches a runtime whose config produces
the same hash (default agent config); other configs must bake.

Also writes navmesh/index.json (era, continent -> hash, zip, tiles, bytes) that the
PowerShell downloader reads.

CREDENTIALS via env (upload only, never on the command line):
    R2_ACCOUNT_ID, R2_ACCESS_KEY_ID|R2_AC, R2_SECRET_ACCESS_KEY|R2_SAK, R2_BUCKET
  optional: R2_ENDPOINT, R2_PREFIX (default: navmesh)

REQUIREMENTS  pip install boto3
USAGE   python scripts/upload-navmesh-r2.py             # every continent/hash present
        python scripts/upload-navmesh-r2.py --dry-run
        python scripts/upload-navmesh-r2.py --continent Northrend
"""
import os, sys, json, argparse, zipfile, tempfile

try:
    import boto3
    from botocore.config import Config
    from botocore.exceptions import ClientError
except ImportError:
    sys.exit("boto3 required: pip install boto3")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "Json", "PathInfo", "navmesh")


def env(name, required=True, default=None):
    v = os.environ.get(name, default)
    if required and not v:
        sys.exit(f"missing env var {name}")
    return v


def make_client():
    account = env("R2_ACCOUNT_ID")
    endpoint = os.environ.get("R2_ENDPOINT", f"https://{account}.r2.cloudflarestorage.com")
    access = os.environ.get("R2_ACCESS_KEY_ID") or os.environ.get("R2_AC")
    secret = os.environ.get("R2_SECRET_ACCESS_KEY") or os.environ.get("R2_SAK")
    if not access:
        sys.exit("missing R2_ACCESS_KEY_ID (or R2_AC)")
    if not secret:
        sys.exit("missing R2_SECRET_ACCESS_KEY (or R2_SAK)")
    return boto3.client(
        "s3", endpoint_url=endpoint,
        aws_access_key_id=access, aws_secret_access_key=secret,
        region_name="auto",
        config=Config(retries={"max_attempts": 5, "mode": "standard"}),
    )


def find_bundles(continent, era):
    """-> [(era, continent, hash, dir, [dnm files])] for every
    <era>/<c>/<hash>/ with tiles. Layout is era-partitioned (precata/cata/...)
    to match DataConfig.Navmesh."""
    out = []
    if not os.path.isdir(SRC):
        return out
    for e in sorted(os.listdir(SRC)):
        if era and e != era:
            continue
        edir = os.path.join(SRC, e)
        if not os.path.isdir(edir):
            continue
        for c in sorted(os.listdir(edir)):
            if continent and c != continent:
                continue
            cdir = os.path.join(edir, c)
            if not os.path.isdir(cdir):
                continue
            for h in sorted(os.listdir(cdir)):
                hdir = os.path.join(cdir, h)
                if not os.path.isdir(hdir):
                    continue
                tiles = [f for f in os.listdir(hdir) if f.endswith(".dnm")]
                if tiles:
                    out.append((e, c, h, hdir, tiles))
    return out


def fetch_index(client, bucket, key):
    """The index describes EVERY bundle in the bucket, not just this run's. An
    era- or continent-scoped upload must merge into it rather than replace it -
    publishing only `--era cata` over a full index would strip the precata
    entries and break download-navmesh.ps1 for everyone already using them."""
    try:
        body = client.get_object(Bucket=bucket, Key=key)["Body"].read()
        existing = json.loads(body.decode("utf-8")).get("continents", [])
        print(f"merging into existing {key} ({len(existing)} entries)")
        return existing
    except ClientError:
        print(f"no existing {key}; writing a fresh one")
        return []
    except (ValueError, KeyError) as e:
        sys.exit(f"existing {key} is unreadable ({e}) - refusing to overwrite it blindly")


def write_index(client, bucket, ikey, entries):
    index = {"continents": [entries[k]
                            for k in sorted(entries, key=lambda k: tuple(p or "" for p in k))]}
    client.put_object(
        Bucket=bucket, Key=ikey,
        Body=json.dumps(index, indent=2).encode("utf-8"),
        ContentType="application/json", CacheControl="public, max-age=300",
    )
    print(f"uploaded {ikey} ({len(index['continents'])} entries total)")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--continent", default="", help="only this continent (e.g. Northrend)")
    ap.add_argument("--era", default="", help="only this era (e.g. precata)")
    ap.add_argument("--skip-unchanged", action="store_true",
                    help="skip bundles already in index.json with the same tile count; "
                         "use when adding continents to an era that is already published")
    ap.add_argument("--index-only", action="store_true",
                    help="rebuild index.json from the bundles already in the bucket; "
                         "uploads no zips. Use after losing the index - a normal run "
                         "only records what it uploads, so rebuilding it any other way "
                         "means re-pushing every bundle.")
    args = ap.parse_args()

    prefix = os.environ.get("R2_PREFIX", "navmesh").strip("/")
    bundles = find_bundles(args.continent, args.era)
    if not bundles:
        sys.exit(f"no navmesh bundles under {SRC} - bake first (--bake=all)")

    print(f"source : {SRC}")
    for e, c, h, _, tiles in bundles:
        print(f"  {e}/{c}/{h}: {len(tiles)} tiles")
    if args.dry_run:
        print("(dry-run) nothing uploaded")
        return

    bucket = env("R2_BUCKET")
    client = make_client()

    ikey = f"{prefix}/index.json"
    entries = {(x.get("era"), x.get("name"), x.get("hash")): x
               for x in fetch_index(client, bucket, ikey)}

    if args.index_only:
        # Describe what is already in the bucket. Tile counts come from the local
        # bundle dirs, sizes from the objects themselves, so the published numbers
        # match what a downloader actually receives.
        for e, c, h, _, tiles in bundles:
            key = f"{prefix}/{e}/{c}/{h}.zip"
            try:
                size = client.head_object(Bucket=bucket, Key=key)["ContentLength"]
            except ClientError:
                print(f"  {e}/{c}/{h}: {key} not in the bucket, skipping "
                      f"(upload it with --era {e} --continent {c})")
                continue

            entries[(e, c, h)] = {
                "era": e, "name": c, "hash": h, "zip": key,
                "tiles": len(tiles), "bytes": size,
            }
            print(f"  {e}/{c}/{h}: {len(tiles)} tiles, {size / 1048576:.0f} MB")

        write_index(client, bucket, ikey, entries)
        return

    for e, c, h, hdir, tiles in bundles:
        # Adding a continent to a published era otherwise re-zips and re-uploads every
        # unchanged bundle - gigabytes for no gain, and it resets the CDN cache on keys
        # whose contents did not change. Tile count is the check: the hash already
        # covers the bake parameters, so same hash + same count means same bundle.
        if args.skip_unchanged:
            prev = entries.get((e, c, h))
            if prev is not None and prev.get("tiles") == len(tiles):
                print(f"  {e}/{c}/{h}: unchanged ({len(tiles)} tiles), skipping")
                continue

        zpath = os.path.join(tempfile.gettempdir(), f"navmesh-{e}-{c}-{h}.zip")
        print(f"zipping {e}/{c}/{h} ({len(tiles)} tiles)...")
        with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
            for f in tiles:
                z.write(os.path.join(hdir, f), f"{e}/{c}/{h}/{f}")
        size = os.path.getsize(zpath)

        key = f"{prefix}/{e}/{c}/{h}.zip"
        print(f"  uploading {key} ({size / 1048576:.0f} MB)")
        client.upload_file(zpath, bucket, key, ExtraArgs={
            "ContentType": "application/zip",
            "CacheControl": "public, max-age=86400",
        })
        os.remove(zpath)

        entries[(e, c, h)] = {
            "era": e, "name": c, "hash": h, "zip": key, "tiles": len(tiles), "bytes": size,
        }

    write_index(client, bucket, ikey, entries)
    print("done")


if __name__ == "__main__":
    main()
