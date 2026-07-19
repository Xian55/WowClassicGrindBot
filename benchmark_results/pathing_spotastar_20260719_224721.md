# PathingAPI Benchmark - SpotAStar

| Setting | Value |
|---|---|
| Date | 2026-07-19 22:47:21 |
| Base URL | http://127.0.0.1:5001 |
| Routes | 36 |
| Iterations | 4 (1 cold + 3 warm) |
| Cold timings valid | True |
| Total duration | 24.3s |

## Cold (first query after Reset, includes chunk load)

| Min | Max | Avg | Median | P95 | P99 | Samples |
|---|---|---|---|---|---|---|
| 426.4ms | 1317.4ms | 645.7ms | 599.8ms | 1018.1ms | 1317.4ms | 31 |

## Warm

| Min | Max | Avg | Median | P95 | P99 | Samples |
|---|---|---|---|---|---|---|
| 0.2ms | 81.1ms | 20.7ms | 21.2ms | 63.1ms | 81.1ms | 93 |

## Failed

- Ammen Vale: HTTP InternalServerError
- Hellfire 1: HTTP InternalServerError
- Hellfire 2: HTTP InternalServerError
- Zul'Drak stairs: HTTP InternalServerError
- Zul'Drak stairs reverse: HTTP InternalServerError

## Endpoint not reached (last point > 5yd from target)

- Z Orgrimmar to vendor: 0 points
- Z Teldrassil to vendor: 0 points
- Z Honor hold issue no result: 1 points
- Cross-zone Elwynn to Redridge: 0 points

## Per-route results

| Route | Cold ms | Warm Min | Warm Avg | Warm Med | Warm Max | Pts | Len yd | Corners | Max Crn deg | Reached |
|---|---|---|---|---|---|---|---|---|---|---|
| Elwynn vendor to path | 1018.1 | 18.1 | 23.3 | 21.3 | 30.5 | 134 | 192.9 | 57 | 135.0 | yes |
| Z Elwynn vendor to path | 939.4 | 21.2 | 23.3 | 22.8 | 26.1 | 134 | 192.9 | 57 | 135.0 | yes |
| Z Coldridge to 5_gnome | 1317.4 | 65.1 | 73.2 | 73.2 | 81.1 | 915 | 1354.7 | 374 | 135.0 | yes |
| Redridge Grave to North | 730.5 | 52.7 | 61.3 | 63.1 | 68.0 | 728 | 193.7 | 336 | 180.0 | n/a |
| Alterac Mountains Horde grave | 590.0 | 9.7 | 15.5 | 10.0 | 26.8 | 702 | 1052.6 | 258 | 135.0 | yes |
| Z Durotar Sen'jin village to grind | 844.9 | 57.3 | 58.3 | 57.7 | 59.8 | 851 | 1265.3 | 504 | 136.8 | yes |
| Durotar Sen'jin village to grind | 836.4 | 37.5 | 39.0 | 38.5 | 41.0 | 815 | 1206.3 | 479 | 136.8 | yes |
| Z Orgrimmar to vendor | 599.8 | 0.7 | 1.5 | 0.9 | 2.9 | 0 | 0.0 | 0 | 0.0 | NO |
| Z Teldrassil to vendor | 519.7 | 0.8 | 1.0 | 0.9 | 1.3 | 0 | 0.0 | 0 | 0.0 | NO |
| Tanaris FP to ZF | 625.7 | 17.4 | 19.9 | 17.5 | 24.7 | 1047 | 330.5 | 479 | 146.3 | n/a |
| Silithus Inn to AQ | 716.9 | 23.6 | 24.7 | 24.5 | 25.9 | 1392 | 257.2 | 589 | 123.7 | n/a |
| Kalimdor Search Barrens | 671.7 | 21.2 | 22.7 | 21.6 | 25.3 | 1363 | 2015.4 | 537 | 135.0 | yes |
| Azeroth Dun morogh 1 | 706.5 | 27.7 | 33.9 | 30.7 | 43.4 | 548 | 808.8 | 272 | 135.0 | yes |
| Dun morogh Vendor to grind | 543.2 | 14.0 | 14.8 | 14.6 | 15.8 | 257 | 339.2 | 83 | 135.0 | yes |
| Azeroth Dun morogh 2 | 530.3 | 5.8 | 6.0 | 5.9 | 6.5 | 461 | 666.1 | 123 | 135.0 | yes |
| Z Dun morogh Vendor Rybrad Coldbank | 716.4 | 29.2 | 30.0 | 30.4 | 30.5 | 380 | 529.3 | 83 | 135.0 | yes |
| Azeroth Dun morogh Vendor Rybrad Coldbank | 642.0 | 28.9 | 29.8 | 29.6 | 31.0 | 380 | 529.3 | 83 | 135.0 | yes |
| Z Azshara issue no result | 426.4 | 0.9 | 1.0 | 1.0 | 1.0 | 32 | 48.6 | 15 | 135.0 | yes |
| Z Honor hold issue no result | 482.6 | 0.2 | 0.2 | 0.2 | 0.2 | 1 | 0.0 | 0 | 0.0 | NO |
| Duskwood issue | 465.5 | 1.0 | 1.0 | 1.0 | 1.1 | 68 | 8.7 | 18 | 123.7 | n/a |
| Loch Modan building Yanni Stoutheart | 519.2 | 23.4 | 24.2 | 24.1 | 25.2 | 119 | 36.1 | 51 | 175.2 | n/a |
| Loch Modan building innkeeper | 519.3 | 34.0 | 35.1 | 35.0 | 36.3 | 133 | 50.5 | 65 | 176.9 | n/a |
| Loch Modan building Vidra Heartstove | 506.2 | 21.2 | 21.4 | 21.4 | 21.5 | 113 | 32.8 | 43 | 175.2 | n/a |
| Dun morogh building Grundel Harkin | 614.1 | 7.7 | 9.2 | 9.0 | 11.1 | 0 | 0.0 | 0 | 0.0 | n/a |
| Dun morogh building Grundel Harkin reverse | 552.6 | 0.4 | 0.4 | 0.4 | 0.5 | 0 | 0.0 | 0 | 0.0 | n/a |
| Dun morogh Coldridge pass - unable to find | 594.8 | 27.6 | 28.0 | 28.2 | 28.4 | 557 | 152.9 | 273 | 146.3 | n/a |
| Dun morogh Coldridge pass | 607.9 | 28.2 | 28.4 | 28.2 | 28.7 | 548 | 153.4 | 272 | 146.3 | n/a |
| Cross-zone Elwynn to Redridge | 779.0 | 0.2 | 0.3 | 0.3 | 0.4 | 0 | 0.0 | 0 | 0.0 | NO |
| Hinterlands 1 water issue | 444.3 | 0.6 | 0.7 | 0.7 | 0.8 | 37 | 10.3 | 13 | 146.3 | n/a |
| Hinterlands 2 water issue | 467.1 | 2.1 | 2.4 | 2.1 | 3.1 | 114 | 14.8 | 63 | 146.3 | n/a |
| Azshara 1 | 490.5 | 8.7 | 10.2 | 10.1 | 11.9 | 389 | 181.1 | 166 | 112.6 | n/a |
| Ammen Vale | FAIL | - | - | - | - | - | - | - | - | HTTP InternalServerError |
| Hellfire 1 | FAIL | - | - | - | - | - | - | - | - | HTTP InternalServerError |
| Hellfire 2 | FAIL | - | - | - | - | - | - | - | - | HTTP InternalServerError |
| Zul'Drak stairs | FAIL | - | - | - | - | - | - | - | - | HTTP InternalServerError |
| Zul'Drak stairs reverse | FAIL | - | - | - | - | - | - | - | - | HTTP InternalServerError |
