# PathingAPI Benchmark - spot-astar-dev-baseline

| Setting | Value |
|---|---|
| Date | 2026-07-19 21:25:46 |
| Base URL | http://127.0.0.1:5001 |
| Routes | 30 |
| Iterations | 4 (1 cold + 3 warm) |
| Cold timings valid | True |
| Total duration | 25.0s |

## Cold (first query after Reset, includes chunk load)

| Min | Max | Avg | Median | P95 | P99 | Samples |
|---|---|---|---|---|---|---|
| 411.1ms | 3014.2ms | 813.9ms | 647.0ms | 1930.3ms | 3014.2ms | 25 |

## Warm

| Min | Max | Avg | Median | P95 | P99 | Samples |
|---|---|---|---|---|---|---|
| 0.2ms | 723.4ms | 30.4ms | 20.6ms | 57.8ms | 723.4ms | 75 |

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

## Per-route results

| Route | Cold ms | Warm Min | Warm Avg | Warm Med | Warm Max | Pts | Len yd | Corners | Max Crn deg | Reached |
|---|---|---|---|---|---|---|---|---|---|---|
| Elwynn vendor to path | 1173.0 | 17.4 | 28.0 | 28.9 | 37.8 | 134 | 192.9 | 57 | 135.0 | yes |
| Z Elwynn vendor to path | 917.4 | 20.1 | 22.9 | 24.1 | 24.4 | 134 | 192.9 | 57 | 135.0 | yes |
| Z Coldridge to 5_gnome | 1930.3 | 48.7 | 60.8 | 57.8 | 75.8 | 915 | 1355.0 | 377 | 135.0 | yes |
| Redridge Grave to North | 3014.2 | 51.6 | 57.6 | 53.8 | 67.3 | 973 | 318.5 | 463 | 180.0 | n/a |
| Alterac Mountains Horde grave | 604.5 | 8.9 | 12.7 | 9.3 | 19.9 | 702 | 1052.6 | 258 | 135.0 | yes |
| Z Durotar Sen'jin village to grind | 898.5 | 53.8 | 277.3 | 54.8 | 723.4 | 0 | 0.0 | 0 | 0.0 | NO |
| Durotar Sen'jin village to grind | 767.9 | 36.1 | 36.4 | 36.3 | 36.8 | 815 | 1206.3 | 479 | 136.8 | yes |
| Z Orgrimmar to vendor | 566.8 | 0.6 | 1.3 | 0.7 | 2.5 | 0 | 0.0 | 0 | 0.0 | NO |
| Z Teldrassil to vendor | 562.3 | 0.9 | 1.1 | 1.1 | 1.3 | 0 | 0.0 | 0 | 0.0 | NO |
| Tanaris FP to ZF | 834.6 | 18.6 | 20.0 | 20.0 | 21.6 | 1048 | 330.5 | 483 | 146.3 | n/a |
| Silithus Inn to AQ | 894.9 | 22.8 | 29.1 | 25.0 | 39.4 | 1449 | 264.7 | 700 | 123.7 | n/a |
| Kalimdor Search Barrens | 701.2 | 20.6 | 25.5 | 21.9 | 34.2 | 1363 | 2015.4 | 537 | 135.0 | yes |
| Azeroth Dun morogh 1 | 692.8 | 27.9 | 28.7 | 28.4 | 29.9 | 548 | 808.8 | 272 | 135.0 | yes |
| Dun morogh Vendor to grind | 647.0 | 13.8 | 13.9 | 13.9 | 14.0 | 255 | 335.8 | 80 | 135.0 | yes |
| Azeroth Dun morogh 2 | 490.2 | 5.8 | 5.9 | 5.8 | 6.1 | 461 | 666.1 | 123 | 135.0 | yes |
| Z Dun morogh Vendor Rybrad Coldbank | 995.9 | 29.6 | 35.1 | 32.3 | 43.4 | 381 | 530.5 | 85 | 135.0 | yes |
| Azeroth Dun morogh Vendor Rybrad Coldbank | 685.1 | 28.8 | 30.5 | 30.9 | 31.7 | 380 | 529.3 | 83 | 135.0 | yes |
| Z Azshara issue no result | 439.0 | 0.6 | 0.6 | 0.6 | 0.7 | 32 | 47.9 | 15 | 135.0 | yes |
| Z Honor hold issue no result | 546.0 | 0.2 | 0.2 | 0.2 | 0.3 | 1 | 0.0 | 0 | 0.0 | NO |
| Duskwood issue | 426.3 | 1.0 | 1.0 | 1.0 | 1.0 | 68 | 8.7 | 18 | 123.7 | n/a |
| Dun morogh Coldridge pass - unable to find | 608.2 | 28.6 | 30.8 | 30.3 | 33.5 | 557 | 152.9 | 273 | 146.3 | n/a |
| Dun morogh Coldridge pass | 627.0 | 27.6 | 28.0 | 27.9 | 28.5 | 548 | 153.4 | 272 | 146.3 | n/a |
| Hinterlands 1 water issue | 411.1 | 0.6 | 1.1 | 0.6 | 2.1 | 41 | 10.7 | 14 | 146.3 | n/a |
| Hinterlands 2 water issue | 432.7 | 2.0 | 2.2 | 2.1 | 2.3 | 114 | 14.8 | 63 | 146.3 | n/a |
| Azshara 1 | 481.7 | 8.5 | 8.7 | 8.5 | 9.1 | 389 | 181.1 | 166 | 112.6 | n/a |
| Ammen Vale | FAIL | - | - | - | - | - | - | - | - | HTTP InternalServerError |
| Hellfire 1 | FAIL | - | - | - | - | - | - | - | - | HTTP InternalServerError |
| Hellfire 2 | FAIL | - | - | - | - | - | - | - | - | HTTP InternalServerError |
| Zul'Drak stairs | FAIL | - | - | - | - | - | - | - | - | HTTP InternalServerError |
| Zul'Drak stairs reverse | FAIL | - | - | - | - | - | - | - | - | HTTP InternalServerError |
