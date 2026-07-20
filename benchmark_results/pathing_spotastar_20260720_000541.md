# PathingAPI Benchmark - SpotAStar

| Setting | Value |
|---|---|
| Date | 2026-07-20 00:05:41 |
| Base URL | http://127.0.0.1:5001 |
| Routes | 36 |
| Iterations | 4 (1 cold + 3 warm) |
| Cold timings valid | True |
| Total duration | 31.7s |

## Cold (first query after Reset, includes chunk load)

| Min | Max | Avg | Median | P95 | P99 | Samples |
|---|---|---|---|---|---|---|
| 446.6ms | 1918.4ms | 719.2ms | 643.1ms | 1312.4ms | 1918.4ms | 31 |

## Warm

| Min | Max | Avg | Median | P95 | P99 | Samples |
|---|---|---|---|---|---|---|
| 0.3ms | 766.8ms | 74.3ms | 75.7ms | 107.1ms | 766.8ms | 93 |

## Failed

- Ammen Vale: HTTP InternalServerError
- Hellfire 1: HTTP InternalServerError
- Hellfire 2: HTTP InternalServerError
- Zul'Drak stairs: HTTP InternalServerError
- Zul'Drak stairs reverse: HTTP InternalServerError

## Endpoint not reached (last point > 5yd from target)

- Z Durotar Sen'jin village to grind: 0 points
- Z Orgrimmar to vendor: 0 points
- Z Teldrassil to vendor: 0 points
- Z Honor hold issue no result: 1 points
- Cross-zone Elwynn to Redridge: 0 points

## Per-route results

| Route | Cold ms | Warm Min | Warm Avg | Warm Med | Warm Max | Pts | Len yd | Corners | Max Crn deg | Reached |
|---|---|---|---|---|---|---|---|---|---|---|
| Elwynn vendor to path | 801.5 | 54.0 | 55.4 | 54.6 | 57.6 | 140 | 206.8 | 83 | 135.0 | yes |
| Z Elwynn vendor to path | 795.1 | 55.0 | 55.3 | 55.0 | 56.0 | 138 | 203.1 | 79 | 135.0 | yes |
| Z Coldridge to 5_gnome | 1918.4 | 98.9 | 112.8 | 99.4 | 140.1 | 927 | 1365.1 | 384 | 135.0 | yes |
| Redridge Grave to North | 1312.4 | 100.6 | 123.2 | 132.1 | 137.0 | 775 | 262.2 | 416 | 180.0 | n/a |
| Alterac Mountains Horde grave | 643.1 | 60.3 | 66.7 | 60.7 | 79.0 | 705 | 1056.2 | 268 | 135.0 | yes |
| Z Durotar Sen'jin village to grind | 902.9 | 105.9 | 326.6 | 107.1 | 766.8 | 0 | 0.0 | 0 | 0.0 | NO |
| Durotar Sen'jin village to grind | 753.0 | 86.0 | 86.6 | 86.0 | 87.8 | 815 | 1206.3 | 479 | 136.8 | yes |
| Z Orgrimmar to vendor | 535.9 | 0.7 | 1.5 | 0.9 | 3.0 | 0 | 0.0 | 0 | 0.0 | NO |
| Z Teldrassil to vendor | 474.3 | 1.0 | 1.1 | 1.1 | 1.1 | 0 | 0.0 | 0 | 0.0 | NO |
| Tanaris FP to ZF | 849.1 | 67.1 | 68.7 | 69.0 | 70.2 | 1048 | 330.5 | 483 | 146.3 | n/a |
| Silithus Inn to AQ | 802.5 | 76.4 | 83.6 | 80.1 | 94.1 | 1449 | 264.7 | 700 | 123.7 | n/a |
| Kalimdor Search Barrens | 781.7 | 72.7 | 81.9 | 85.9 | 87.2 | 1363 | 2015.4 | 537 | 135.0 | yes |
| Azeroth Dun morogh 1 | 625.3 | 78.3 | 78.9 | 78.4 | 79.9 | 548 | 808.8 | 272 | 135.0 | yes |
| Dun morogh Vendor to grind | 664.1 | 63.5 | 63.9 | 64.0 | 64.1 | 255 | 335.8 | 76 | 135.0 | yes |
| Azeroth Dun morogh 2 | 546.3 | 56.1 | 69.2 | 57.4 | 94.3 | 465 | 668.8 | 126 | 135.0 | yes |
| Z Dun morogh Vendor Rybrad Coldbank | 1045.0 | 79.7 | 81.4 | 81.3 | 83.1 | 381 | 530.5 | 87 | 135.0 | yes |
| Azeroth Dun morogh Vendor Rybrad Coldbank | 689.3 | 78.8 | 78.9 | 78.9 | 79.1 | 380 | 529.3 | 85 | 135.0 | yes |
| Z Azshara issue no result | 469.7 | 101.0 | 101.0 | 101.0 | 101.1 | 32 | 47.9 | 15 | 135.0 | yes |
| Z Honor hold issue no result | 570.0 | 100.2 | 101.9 | 102.1 | 103.3 | 1 | 0.0 | 0 | 0.0 | NO |
| Duskwood issue | 456.9 | 50.7 | 51.9 | 52.0 | 52.9 | 68 | 8.7 | 18 | 123.7 | n/a |
| Loch Modan building Yanni Stoutheart | 767.4 | 73.1 | 74.3 | 74.1 | 75.7 | 121 | 36.0 | 47 | 175.2 | n/a |
| Loch Modan building innkeeper | 614.2 | 83.2 | 84.8 | 85.1 | 85.9 | 132 | 47.0 | 60 | 176.0 | n/a |
| Loch Modan building Vidra Heartstove | 547.4 | 71.5 | 72.2 | 72.3 | 72.7 | 113 | 32.8 | 43 | 175.2 | n/a |
| Dun morogh building Grundel Harkin | 542.2 | 6.5 | 6.7 | 6.8 | 6.8 | 0 | 0.0 | 0 | 0.0 | n/a |
| Dun morogh building Grundel Harkin reverse | 545.7 | 0.4 | 0.5 | 0.4 | 0.6 | 0 | 0.0 | 0 | 0.0 | n/a |
| Dun morogh Coldridge pass - unable to find | 625.7 | 78.1 | 78.9 | 79.0 | 79.8 | 557 | 152.9 | 273 | 146.3 | n/a |
| Dun morogh Coldridge pass | 636.2 | 78.3 | 78.4 | 78.4 | 78.6 | 548 | 153.3 | 272 | 146.3 | n/a |
| Cross-zone Elwynn to Redridge | 720.4 | 0.3 | 0.4 | 0.4 | 0.5 | 0 | 0.0 | 0 | 0.0 | NO |
| Hinterlands 1 water issue | 446.6 | 100.7 | 101.6 | 101.3 | 103.0 | 41 | 10.7 | 14 | 146.3 | n/a |
| Hinterlands 2 water issue | 494.2 | 52.1 | 52.3 | 52.2 | 52.6 | 114 | 14.8 | 63 | 146.3 | n/a |
| Azshara 1 | 719.4 | 59.3 | 63.8 | 59.5 | 72.5 | 382 | 182.0 | 150 | 112.6 | n/a |
| Ammen Vale | FAIL | - | - | - | - | - | - | - | - | HTTP InternalServerError |
| Hellfire 1 | FAIL | - | - | - | - | - | - | - | - | HTTP InternalServerError |
| Hellfire 2 | FAIL | - | - | - | - | - | - | - | - | HTTP InternalServerError |
| Zul'Drak stairs | FAIL | - | - | - | - | - | - | - | - | HTTP InternalServerError |
| Zul'Drak stairs reverse | FAIL | - | - | - | - | - | - | - | - | HTTP InternalServerError |
