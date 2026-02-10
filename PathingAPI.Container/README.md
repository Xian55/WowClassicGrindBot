# PathingAPI.Container

Container entrypoint for running PathingAPI on Linux. It reuses the existing PathingAPI services/components and exposes them on `http://0.0.0.0:5001` by default so the service is reachable outside the container.

## Build
```bash
docker build -f PathingAPI.Container/Dockerfile -t pathingapi-linux .
```
The Dockerfile now builds StormLib from source inside the image so MPQ archives can be read on Linux (`libstorm.so` is copied into the published output).

## Run
```bash
docker run --rm -p 5001:5001 -e HOST_URL=http://0.0.0.0:5001 pathingapi-linux
```
- MPQ data is **not** baked into the image. Mount only your MPQ folder, e.g.:
```bash
docker run --rm -p 5001:5001 -e HOST_URL=http://0.0.0.0:5001 -v /host/MPQ:/json/MPQ pathingapi-linux
```
- Use `HOST_URL` (or `ASPNETCORE_URLS`) to override the binding address/port.
- The service validates that the configured MPQ path (default `/json/MPQ` from `data_config.json`) exists and contains `.MPQ` files on startup.
