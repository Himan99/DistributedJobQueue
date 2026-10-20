# SPEC-005 — Exponential Backoff with Jitter

**ID:** SPEC-005  
**Title:** Exponential backoff with jitter on Nack retry  
**Status:** Implemented  
**Type:** Enhancement  
**Architecture ref:** Section 3 — `run_after = now() + backoff(attempts)  # e.g. exponential + jitter`

## Problem
`PostgresStore.Nack` currently uses a fixed `interval '1 seconds'` backoff for all retry attempts, regardless of attempt count. The architecture specifies exponential backoff + jitter.

## Requirements
1. `run_after = now() + backoff(attempts)` where `backoff` grows exponentially with the attempt count.
2. Add jitter (randomization) to prevent thundering herd when many jobs become eligible simultaneously.
3. Cap the maximum backoff to avoid unbounded delays.
4. Example formula: `backoff(attempt) = min(base * 2^(attempt-1), max_cap) + random_jitter(0, jitter_max)`

## Files affected
- `BrokerService/Persistence/PostgresStore.cs` — modify `Nack` SQL to compute exponential backoff instead of fixed 1 second
- May extract backoff calculation into a helper or configure via parameters

## Verification
- First retry (`attempts=1`) → short backoff (e.g., ~1s)
- Second retry (`attempts=2`) → longer backoff (e.g., ~2s+)
- Third retry (`attempts=3`) → even longer, capped at max
- Backoff values vary slightly due to jitter (not identical across jobs)
