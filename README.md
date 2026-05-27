# LargePrimeCli

LargePrimeCli is a .NET 8 command-line toolkit for experimenting with large-prime generation, wheel-based candidate sieving, integer factorization, and primality proving workflows inspired by Albert Meyburgh's paper, **A Roots-of-Unity Interpretation of Wheel Sieving and Pollard p-1**.

- [Read the paper PDF](docs/paper/root_unity_wheel_paper.albert_meyburgh.pdf)

## Paper summary

The paper gives an algebraic interpretation of ordinary wheel sieving. For a squarefree wheel modulus

```math
M = \prod_{p \in B} p
```

built from a fixed set of small primes `B`, the admissible wheel residues modulo `M` are exactly the exponents that generate primitive `M`-th roots of unity:

```math
\gcd(r, M) = 1 \quad\Longleftrightarrow\quad e^{2\pi i r/M}\text{ is primitive.}
```

This is a reformulation of the classical coprimality filter, not a claim that complex arithmetic should be used in the hot path. The practical implementation should precompute admissible residues and cyclic gap tables, then generate candidates with integer arithmetic.

The paper also shows that a natural "root-collision" criterion reduces exactly to divisibility by previously selected primes. In implementation terms, storing rational angles or root identifiers is equivalent to checking whether a candidate is divisible by one of the primes in the wheel basis.

A second viewpoint expresses the same wheel mask using Ramanujan sums. This gives a compact spectral description of the periodic keep/reject pattern while preserving the same computational conclusion: the efficient code path is ordinary modular arithmetic, cached wheels, and direct divisibility filters.

The Pollard `p - 1` section uses the same roots-of-unity language to explain why smooth exponent schedules expose factors. If one hidden prime factor has smooth `p - 1`, repeated powering can collapse a residue to the identity modulo that factor, and `gcd(a^M - 1, n)` reveals a non-trivial divisor. The implementation therefore uses cached prime-power schedules, stage-2 extension, and conventional factorization fallbacks.

## Project features

- Generate large probable primes of a requested bit length.
- Build reusable prime caches with a segmented sieve.
- Factor integers using:
  - cached small-prime trial division,
  - optional large-prime cache trial division,
  - a cached Pollard `p - 1` prime-power/root schedule with streamed segmented-sieve stage 2,
  - Pollard rho fallback.
- Run probable-prime checks with randomized Miller-Rabin or Baillie-PSW.
- Attempt recursive Pocklington proofs for returned prime factors with `--prove`.
- Output generated primes in decimal or hexadecimal.

Primality note: by default the CLI uses probable-prime tests. Randomized Miller-Rabin confidence depends on the number of rounds (`--rounds` for prime generation, `--miller-rabin-rounds` for factorization). `--baillie-psw` is available for factorization probable-prime checks. `--prove` attempts recursive Pocklington proofs for returned factors; ECPP fallback is not implemented yet.

## Requirements

- .NET 8 SDK

## Build

```bash
dotnet build
```

Create a release publish folder:

```bash
dotnet publish -c Release -o ./publish
```

## Generate primes

Generate one 256-bit prime:

```bash
dotnet run -- --bits 256
```

Generate two 512-bit primes in hexadecimal:

```bash
dotnet run -- -b 512 -f hex -c 2
```

By default the generator looks for cached inputs at:

- `.prime-cache/small-primes.txt`
- `.prime-cache/large-primes.txt`

If the small-prime cache is missing, it generates small primes in memory up to `--small-prime-limit`.

## Build prime caches

Enumerate primes up to `N` with the segmented sieve and write cache files:

```bash
dotnet run -- cache --max 1000000
```

Custom output paths:

```bash
dotnet run -- cache --max 1000000 \
  --small-out ./cache/small-primes.txt \
  --large-out ./cache/large-primes.txt
```

Use those cache files during generation:

```bash
dotnet run -- --bits 256 \
  --small-primes-file ./cache/small-primes.txt \
  --large-primes-file ./cache/large-primes.txt
```

## Factor integers

Factor `8051`:

```bash
dotnet run -- factor 8051
```

Expected factors:

```text
83
97
```

Factor output is streamed to stdout as factors are discovered; the order is not guaranteed when parallel workers are used. The in-process factor list is sorted before it is returned.

From a published build:

```bash
dotnet ./publish/LargePrimeCli.dll factor 8051
```

Useful factorization options:

```text
--pminus1-bound <n>           Stage-1 smoothness bound for Pollard p-1/root collision search
--pminus1-stage2-bound <n>    Stage-2 bound for one-large-prime p-1 extension
--root-schedule-file <path>   Cache file for reusable Pollard p-1 prime-power/root schedule
--no-large-prime-cache        Skip large-prime cache trial division
--force-large-prime-cache     Scan large-prime cache even for huge inputs
--miller-rabin-rounds <n>     Randomized Miller-Rabin rounds for probable-prime checks
--baillie-psw                 Use Baillie-PSW probable-prime checks instead of randomized Miller-Rabin
--prove                       Prove returned prime factors with recursive Pocklington certificates where possible
-w, --workers <n>             Parallel factor workers
-q, --quiet                   Only print factors to stdout
```

The reusable root schedule defaults to:

```text
.prime-cache/pminus1-powers-<bound>.txt
```

## Command help

```bash
dotnet run -- --help
dotnet run -- cache --help
dotnet run -- factor --help
```

## Regression tests

Run the built-in deterministic regression suite, including wheel, primality, factorization, cache, concurrency, and Brent rho repeated-factor cases:

```bash
dotnet run -- self-test
```
