# Spike S4: lock-free circuit breaker

Question (plan S4): can the circuit-breaker controller be lock-free, with Polly's semantics intact, and
is it faster than Polly's `lock` under contention? Pass: throughput ≥ Polly at 16 threads, and no lost
transitions over 10⁶ fuzzed interleavings. Otherwise keep a lock.

**Status: the correctness half passes. The timing half has not run yet** (other spikes were running in
parallel, so timings would be distorted). Command:

```
cd Resilience\spikes\S4
dotnet run -c Release -- --bench
```

`--bench` runs `--verify --quick` first as a gate, then BenchmarkDotNet over `ClosedStateBenchmarks`. BDN
arguments pass through, e.g. `-- --bench --filter *LockFree*`. Correctness only: `dotnet run -c Release -- --verify`.

## Files

| File | What |
|---|---|
| `LockFree.cs` | `LockFreeCircuitBreaker` (packed state word) + `StripedRollingHealth` (lock-free, striped port of Polly's `RollingHealthMetrics`) |
| `Locked.cs` | `LockedCircuitBreaker`: line-for-line port of Polly's `CircuitStateController` + `AdvancedCircuitBehavior` + `RollingHealthMetrics` under one lock, on the same clock. It is the benchmark's apples-to-apples baseline, and the second parity reference. |
| `PollyAdapter.cs` | A Polly 8.8.0 `ResiliencePipeline<int>` with only a circuit breaker. Failures are handled results (`-1`), so nothing throws. Has manual control and a state provider. |
| `Verify.cs` | Deterministic tests, parity with Polly, and three fuzzers |
| `Benchmarks.cs` | `ClosedStateBenchmarks`: Polly vs Locked vs LockFree × 1/4/16 threads × 0 % / 5 % failures |

## Design

### Circuit state: one `long`, changed only by CAS

```
bits  0..1   CbState (Closed, Open, HalfOpen, Isolated; same values as Polly)
bits  2..15  generation, +1 on every transition
bits 16..63  blocked-until, µs since construction (2^48 µs = 8.9 years; max value = "forever")
```

- **Closed fast path:** `TryEnter` is one `Volatile.Read` and a mask test, with no time read. The state
  word sits on its own 128-byte padded line.
- **Every transition is a `CompareExchange` from the exact word the caller saw.** So exactly one
  thread wins each transition. That winner is the only one that fires the event (Polly's
  "do not duplicate-signal onBreak").
- **Half-open, with only one probe:** in `Open`, once `now ≥ until`, callers race a CAS
  `Open(g) → HalfOpen(g+1, until = now + break)`. The single winner gets `Admission.Probe`. Everyone else
  re-reads, sees `HalfOpen` and is rejected. Polly behaves the same way: while the probe is out, it
  rejects everything. No separate probe flag or counter is needed.
- **The generation defeats ABA.** Take `Closed(g) → Open → HalfOpen → Closed(g')`. Without the
  generation, a thread that computed "should break" against the old closed period could still win its
  CAS. The generation is 14 bits, so the word repeats only after 16 384 transitions. The race needs a
  thread stalled across exactly a multiple of that.
- **Losing a CAS means re-dispatching on the new state, never re-counting.** One example: a closed-state
  failure that loses its break CAS to a half-open probe is not counted again. It is linearized before
  that transition.
- **Time** comes from `TimeProvider.GetTimestamp()` (monotonic), in integer µs. Polly uses `GetUtcNow()`.
  The results are the same under a `FakeTimeProvider`, and wall-clock jumps cannot affect us.

### Health: Polly's rolling windows, lock-free and striped

Polly's `RollingHealthMetrics` anchors a window at the first event. It does not use a fixed grid. A
new window starts when the current one is at least `sampling/10` old, and a window counts while
`now − start < sampling`. The port keeps exactly those rules. The deterministic test T3 has a case
where fixed 100 ms buckets would give a different answer than Polly's event-anchored windows.

- **`_head = (seq:32 | floor:32)`.** `floor > seq` means "no current window" (Polly's
  `_currentWindow == null` after `Reset`). Advancing the head is one CAS.
- **`_starts[t % 16] = (tag:16 | start-µs:48)`.** Each slot is written once, by CAS, *before* the head
  advances to `t`, so a reader never sees a head without its start. Polly never has more than 10 live
  windows (start times are at least `sampling/10` apart), so a 16-slot ring has no live collisions.
  Every window rewrites its slot, so slot tags stay within 16 of the head. That makes the 16-bit
  modular comparison sound.
- **Counters:** there is a success / failure / slow triple per stripe per slot, each stored as
  `(window-seq:32 | count:32)`. When a tag does not match, the first writer of the new window
  CAS-resets the counter; the head is checked first, so a thread holding a stale window number cannot
  wipe a newer window. The tag is the full 32 bits, so a stripe that sat idle for hours cannot
  resurrect old counts. A count saturates at 2^32 per stripe per window. Polly's `int` would overflow
  before that.
- **Stripe = `Thread.GetCurrentProcessorId()`.** The hot success path touches a core-local line. A
  failure in the closed state sums `≤ 16 windows × stripes × 3`, which is 480 reads at 16 stripes. Only
  failures pay this, and Polly re-sums its queue on every failure too. There are
  `min(16, pow2(cores))` stripes, which is about 6 KB per breaker at 16 stripes.

### Reset timing: the one deliberate deviation

Polly resets the metrics under the lock, at the same moment it closes. A CAS cannot publish `Closed` and
clear the window atomically. So consider a thread admitted under the new `Closed` word that records a
failure before the reset lands: it would judge "should break" against the pre-break failures, and could
re-open a circuit that just recovered. To prevent that:

- **The window is also cleared when the probe is admitted** (`Open → HalfOpen`, by the winner). Nothing
  reads the window while the circuit is half-open.
- **It is cleared again after `HalfOpen → Closed`**, which is what Polly does.

In between, the window holds only a handful of late results from the half-open period, so the race
cannot bring back the failures that broke the circuit. Manual `Close` resets before and after its CAS.
Nothing reads the window in `Open` or `HalfOpen`, so none of this can be observed in the state sequence.
The parity test confirms that. The one place it would show is the `BreakDurationGenerator` arguments
(failure rate and count on re-open). If the generator is supported, snapshot those at open time.

The other inherent relaxation: an outcome recorded at the very instant of a transition may land on
either side of it. That is ordinary linearizability, and it does not affect "one probe" or "no lost
transitions".

### Slow-call ratio (resilience4j-style): fits, cheaply

It is implemented here and tested (`SlowCalls`):

- **Counting:** `RecordSuccess(elapsed)` / `RecordFailure(elapsed)` also increment a third counter when
  `elapsed ≥ SlowCallThreshold`.
- **Breaking:** `ShouldBreak` trips on `failures/total ≥ FailureRatio` **or** `slow/total ≥ SlowCallRatio`,
  behind the same minimum throughput.
- **The one new rule:** a slow *success* in `Closed` also runs the break check. Fast successes never
  read the window, so the hot path is unchanged.
- **Half-open:** a slow probe counts as a failed probe and re-opens. resilience4j evaluates N permitted
  half-open calls. Polly-style single-probe semantics make this the natural mapping.
- **Cost:** it adds one counter per slot and nothing on the fast path.

The caller measures elapsed time. The generated pipeline already has a timestamp for timeouts and
telemetry.

## Correctness results

These results are from one full `--verify` run on an 8-core machine (8 fuzz threads). It exited with
code 0 after 59 s, and every check passed.

| Check | Size | Result |
|---|---|---|
| **Deterministic transitions**, on both LockFree and Locked, with `FakeTimeProvider` | T1–T5: closed → open at min throughput + ratio; reject 1 ms before the break ends; probe exactly at the break end; a second caller is rejected while the probe is out; probe success → closed with the window reset; probe failure → open with a fresh break; failures 1000 ms old expire and 999 ms old still count; event-anchored windows (a grid would differ); isolate rejects forever; manual close resets; late success in half-open closes (Polly semantics); late results in open change nothing | pass |
| **Slow-call extension** | trips on slow *successes*; ≥ threshold counts as slow; a slow probe re-opens; a fast probe closes and resets | pass |
| **Parity with Polly 8.8.0**: a real `ResiliencePipeline<int>` with only a circuit breaker, `FakeTimeProvider`, `StateProvider` and `ManualControl`, compared against Locked and LockFree | 3 000 random scripts, 750 000 ops, 34 158 state changes. Each script picks its options at random: ratio 0.1/0.5/0.75/1.0, throughput 2/4/10, sampling 0.5/1/3 s, break 0.5/1 s. Ops are sync calls, in-flight begin/end (late outcomes, via a `TaskCompletionSource` in the callback), clock jumps of 1 ms–1.5 s including exactly one break, isolate and close. LockFree uses 1–8 stripes. | **State and admission identical after every op** |
| **Fuzz A: the half-open instant.** Per round: trip the breaker, set the clock exactly to the end of the break, release every thread through a barrier, and hammer `TryEnter`. | 20 000 rounds × 8 threads × 8 = 1 280 000 racing `TryEnter` | **exactly 1 probe and 0 closed admissions in every round** |
| **Fuzz B: continuous chaos.** 55 % failures, random spins (late outcomes), clock jumps from every thread, and rare Isolate/Close. The winner of every successful CAS logs (from, to). Per round, the log must form one unbroken chain from the start word to the end word, each step legal and gen+1, with probes == Open→HalfOpen transitions and the circuit never left HalfOpen. | 40 rounds × 8 threads, 1 280 000 ops, 21 519 transitions, 7 747 probes | **no lost, duplicated or illegal transitions** |
| **Fuzz C: counters across window starts.** One thread moves time through 9 window starts while 8 threads record. Half the rounds use 1 stripe, so every thread hits one counter. | 12 rounds, 2 400 000 records | **the window total equals exactly what was recorded** |

### Mutation check (does the harness catch real bugs?)

Each mutant was applied to `LockFree.cs`, run with `--verify --quick`, and reverted:

| Mutant | Caught by |
|---|---|
| Open → HalfOpen with a plain write instead of CAS | fuzz A: two or more probes in 85 of 2 000 rounds |
| Counter increment with a plain write instead of CAS | fuzz C, in its 1-stripe rounds (lost increments) |
| No window reset on HalfOpen → Closed | deterministic tests, slow-call tests, parity |
| No head check before resetting a mismatched counter | **not caught**. It only matters when a thread stalls for more than 16 window starts between reading the window number and its CAS. The fuzzers produce at most 9 starts per round. The safety argument is in the design section above. |

Fuzz B did not catch the first mutant within `--quick`. Continuous chaos seldom hits the exact half-open
instant, which is why fuzz A exists.

## What the timing run still has to answer

`ClosedStateBenchmarks`: every invocation runs 2^18 operations split across `Threads` threads, so Mean is
wall time per operation. That makes it a throughput measure: if 16 threads scale, the Mean falls. Rows:

- **Polly:** the full pipeline, `pipeline.Execute(static v => v, x)`. This includes context pooling and
  strategy dispatch around its locked controller. S1 measured about 138 ns for Polly's closed circuit
  breaker.
- **Locked:** Polly's controller logic under the same lock, with no pipeline. This is the clean
  lock-vs-CAS comparison.
- **LockFree:** this spike.

The run has to answer three questions:

1. **Pass criterion:** is LockFree ≥ Polly at 16 threads? Expected to pass by a wide margin, because
   Polly also pays the pipeline cost.
2. **The honest version:** is LockFree ≥ Locked at 16 threads? That is the question the design really
   answers. With 5 % failures, the 480-read snapshot per failure is the most likely place to lose
   ground.
3. **Allocations:** Polly's queue allocates a window object every 3 s, so all rows should show ≈ 0 B/op.

## Recommendation

Correctness does **not** force the fallback. The state machine is small enough to reason about
completely. Every transition is one CAS on one word, and the generation removes ABA. The fuzzers show
exactly one probe per half-open instant, a single unbroken legal chain of transitions, and no lost
increments. Parity with Polly 8.8.0 is exact on random scripts that include in-flight "late" outcomes
and manual control.

- **If the timing run shows LockFree ≥ Locked at 16 threads,** adopt it for M2.
- **If LockFree beats Polly but not Locked,** the win comes from dropping the pipeline and not from
  removing the lock. In that case, **keep a lock**: it is simpler to maintain, and the event-anchored
  health ring is the only intricate part.
- **If only the 5 % failure case loses:** keep the design and cut the cost of the snapshot. Use fewer
  stripes (the constructor takes `stripes`), or keep a per-window running total so a failure reads
  10 values instead of 480.

Either way, the packed state word with the half-open probe CAS is worth keeping. It makes `TryEnter` in
the closed state a single read, with no time lookup.
