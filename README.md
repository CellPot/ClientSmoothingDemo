# ClientSmoothingDemo

A Unity demo that compares client-side network smoothing techniques against one authoritative moving entity, and measures how well each one does under different network conditions.

Unity 6000.3. Networking by [Mirror](https://github.com/MirrorNetworking/Mirror) (included under `Assets/Mirror`).

## Techniques

| Technique | Idea |
|---|---|
| Naive | Snap to the latest snapshot |
| Exponential Smoothing | Low-pass the received position |
| Client-Side Interpolation | Chase the latest snapshot with a fixed lerp speed |
| Snapshot Interpolation (+ Dead Reckoning) | Render slightly in the past between two buffered snapshots; the hybrid extrapolates when the buffer runs dry |
| Jitter Buffer (+ Dead Reckoning) | Sorted buffer with an adaptive playback clock; same fallback |
| Dead Reckoning | Extrapolate with the last velocity and blend toward corrections |
| Kalman (+ Exponential Smoothing) | Per-axis Kalman filter, optionally smoothed again for rendering |
| Client Prediction + Reconciliation | Extrapolate from the last confirmed state and blend toward it (see note below) |

Every technique renders as a coloured dot with a trail; the white diamond is the server.

## Scenes

Both live in `Assets/Scenes` and are supported and benchmarked.

- **MirrorNetwork**: real Mirror networking over KCP, wrapped in Mirror's `LatencySimulation`. The server entity is a prefab spawned by `DemoNetworkManager`.
- **SimulatedNetwork**: a single process with no Mirror networking. A scene-placed server feeds `NetworkSimulator`, which fakes latency, jitter and loss.

Both scenes measure error the same way (see below), so results are comparable, although the loss models differ (KCP vs. the simulator's random drop).

## Running it

### SimulatedNetwork
Open the scene and press Play.

### MirrorNetwork
Two ways to run it:

1. **Host + client in one editor** (Mirror's host button). The local connection bypasses the transport, so there is no latency, loss or unreliable delivery. Fine for a quick look, not for measuring.
2. **Host in one editor, client in a second process** (recommended). With [ParrelSync](https://github.com/VeriorPies/ParrelSync) create a clone, start the host in the main editor and connect the client in the clone (KCP, `localhost:7777`). Run benchmarks from the client.

Notes for the ParrelSync setup:
- A clone only recompiles when its window is focused. Host and client must run the same code, or SyncVar and spawn layouts break.
- `Assets/Plugins/ParrelSync/` is git-ignored; install it yourself.

### Controls
- `Space`: pause (disabled on a remote client: it would only freeze the local view while the server keeps moving). `R`: reset the metrics.
- The on-screen panel shows per-technique error, lets you hide techniques, and has sliders for latency, jitter, loss and send rate, plus presets.
- **Tune** (top bar) opens live sliders for the selected technique's parameters (found by reflection from its `[Range]` floats and bools, so new techniques get them automatically), with a reset to the values at startup. Changes are local to the running client.
- In Mirror the server owns the network conditions; a client changes them through a command (sent at most every 0.1 s while dragging) and every client sees the same values, including changes made by another peer or a benchmark.
- The mode is detected automatically: simulator, Mirror offline, dedicated server, host or client. Server speed / back-move / player-controlled are only shown where the server runs (they are not synced). A dedicated server has no client, so it shows no metrics and cannot run the benchmark. Conditions are locked while a benchmark runs.

## Network presets

| Preset | Latency | Jitter | Loss | Send rate |
|---|---|---|---|---|
| Perfect | 0 ms | 0 | 0% | 20 Hz |
| LAN | 10 ms | 5 | 0% | 20 Hz |
| Broadband | 80 ms | 20 | 2% | 20 Hz |
| Mobile | 150 ms | 60 | 5% | 10 Hz |
| Lossy | 200 ms | 100 | 15% | 10 Hz |
| Chaos | 300 ms | 150 | 25% | 5 Hz |

## How error is measured

- The server stamps snapshots with its own clock. The client rebases them onto its local clock using the least-delayed packet it has seen, so the minimum one-way latency is not counted as error.
- Snapshots are sent unreliably (stale or reordered ones are dropped).
- The server also streams its exact position at 60 Hz (metrics only). The client keeps about 3 s of that history and compares each technique with the true position at the matching local time. The simulator scene builds the same history, shifted by the minimum latency.
- The toggle "Error vs. true path" falls back to the live server transform.
- **Delay-adjusted error** compares against the truth rewound by the delay a technique adds on purpose (buffer depth, or the lag of its smoothing chase), so deliberate buffering is not counted as error.
- **Smoothness** is the frame-to-frame change in velocity (`rms_dv`); a frame above 15 units/s counts as a stutter.
- **Score** = `adj_mean + 0.02 * rms_dv + 0.003 * added_delay_ms` (lower is better). The weights are a judgement call; the ranking is fairly robust to them (Kalman + Exponential Smoothing stays first overall unless delay is made free). The results window has sliders for both weights and an overall ranking (mean score over all presets); the CSV always uses the defaults.

## Benchmark

The benchmark runs every preset in turn (3 s settle, then 20 s of recording) on a seeded path, so runs are repeatable. Per technique it reports mean, p95 and max error, delay-adjusted mean/p95, smoothness, stutters, added delay and the snapshot rate received.

Results appear in a window (sorted by score) and are saved as CSV to
`<persistentDataPath>/benchmarks/` (on Windows: `%USERPROFILE%\AppData\LocalLow\<company>\<product>\benchmarks\`). Keep old CSVs; they are the regression record. Only compare runs made with the same code version.

## Notes

- **Reconciliation:** the server entity moves on its own path, so the benchmark uses the automatic path (extrapolation plus blend). The input-replay path only makes sense with "Player Controlled" on the host machine; clients do not send their inputs to the server.
- **Dead Reckoning hybrids** behave exactly like their base technique on clean links. They only act when the buffer runs dry, which takes several consecutive losses or a gap longer than the buffer.
- The simulator scene's server needs a `NetworkIdentity` (the server entity is a `NetworkBehaviour`), which Mirror's scene post-processing can disable; `DemoUIManager` re-enables it.
