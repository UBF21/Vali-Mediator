# Performance and load testing — summary

This is a distilled summary of local load testing done against the sample API backed by real
Redis + PostgreSQL + Toxiproxy (not mocks). The full raw report (every endpoint, every percentile,
every run) is kept **outside this repository** — it is a one-off measurement on a shared laptop, not
a reproducible benchmark, and its 600+ lines of per-run tables would be noise here. This page has the
methodology and the numbers that matter for a release decision.

## What was measured

Ten load-test runs (k6, open-arrival-rate model, no coordinated omission) against the sample API with
the cache, idempotency and resilience packages wired to real Redis and PostgreSQL:

| Run | Scale | req/s | Purpose |
|---|---|---:|---|
| M1 (×3: net8/net9/net10) | 10,000 products, ~2 ms latency | ≈ 770 | Baseline, cross-runtime parity |
| M2 (host + container) | 200,000 products + 500,000 orders, ~35 ms latency, 100 ms spikes | ≈ 1,540 | More data, more latency |
| M3 (host + container) | **1,000,000 products + 1,000,000 orders**, ~1 ms latency | ≈ 3,300–3,800 | Large catalog, high throughput |
| Soak | 200,000 + 500,000, ~5 ms | ≈ 2,060 for 49.5 min continuous | Memory leak / latency drift over time |
| Stepped saturation | Same catalog, ×1 → ×8 load | 758 → 4,657 | Find the degradation point |
| Two instances | 100,000 + 200,000, shared Redis + PostgreSQL | ≈ 1,570 | Idempotency correctness across processes |

Total across the 10 valid runs: **≈ 14.8 million requests**.

## Key results

**Correctness (the part that matters most for a mediator/idempotency library):**
- 0 duplicate orders, 0 responses served to the wrong user/key, 0 cases where "same key + different
  payload" was wrongly accepted (all correctly rejected with 409), 0 orphaned or inconsistent rows —
  across all ~14.8M requests, verified by reconciling every run against PostgreSQL.
- The two-instance run specifically confirms **idempotency reservations are atomic across processes**
  sharing one Redis: 29,587 confirmed orders = 29,587 distinct (key, scope) rows, 0 duplicates, with
  40% of traffic sending concurrent identical requests split across both instances.

**Memory (49.5 min soak, ~2,060 req/s sustained):**
- Working-set slope after warmup: **−19.6 MB/h** (i.e., no upward trend — no leak signal).
- GC gen2 collections plateau after the first ~15 minutes instead of growing.
- At rest (45s idle) after any run: 0 idempotency locks, 0 coalescing locks, 3 threads, empty queue.

**Throughput / degradation point (stepped saturation, same machine running generator + API + DB):**
- Scales ~linearly to ×3 (≈ 2,245 req/s): p99 read latency only goes from 337 ms to 645 ms.
- **×4 (≈ 2,970 req/s): p99 read latency crosses 2× the baseline** — first degradation signal.
- **×5 (≈ 3,390 req/s): clear degradation** — p99 read 2.9 s, GC pauses 16% of wall time, k6 discards
  10.8% of iterations.
- At ×8 (4,657 req/s achieved) failure rate is 1.14%, but **every unexpected error is a 503/504 from a
  saturated dependency (Redis/PostgreSQL timeout) — zero unexpected 500s** at any load level.
- Recovering to ×0.2 load brings p99 back to baseline and all internal counters to 0.
- The bottleneck in this rig is PostgreSQL CPU and GC pause time under high allocation rate, not the
  mediator/library code — the API process itself never exceeds ~1 CPU core or ~270 MB working set even
  at the highest load step.

**Throughput vs. the previous major version (BenchmarkDotNet, net9.0, in-process, no I/O):**
- `Send` (no behaviors): **≈ 5.2× faster** (3.3 µs → 0.64 µs), **−60% allocation** (1,761 → 696 B/op).
- `Publish` (3 handlers): **≈ 2–2.5× faster**, allocation up ~5% (cost of unifying the handler-resolution
  path).

## Known limitations of this testing round

- One repetition per configuration — no confidence intervals.
- Shared laptop (Intel i7-11800H, 32 GB RAM) running the load generator, the API, and the database at
  once — absolute latency numbers are pessimistic; relative comparisons (runtime vs. runtime, host vs.
  container) are more reliable than the absolute figures.
- Soak is 49.5 minutes, not 8–24 hours; soak/saturation used a 200K-product catalog, not the full 1M
  (the larger catalog exhausted the machine's memory during those specific runs).
- Only net9.0 was benchmarked with BenchmarkDotNet (not net8.0/net10.0).
- Two-instance test: both processes on the same machine, single Redis node, no failover or reservation-
  expiry-under-load scenario.

This is sufficient evidence of **correctness and stability**, not a substitute for a dedicated-hardware
benchmark or a multi-hour soak.

## How to reproduce

Requires Docker, [k6](https://k6.io/), .NET SDK 7–10, and Python 3. Scripts and the full raw report
live outside this repository (local-only, machine-specific paths and credentials). See
[docs/INFRAESTRUCTURA-PRUEBAS.md](INFRAESTRUCTURA-PRUEBAS.md) for the in-repo integration tests that
exercise the same Redis/PostgreSQL code paths (cache hit/miss, distributed idempotency, degradation
under injected latency/outages) in a way anyone can run with `dotnet test`.
