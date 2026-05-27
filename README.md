# LargePrimeCli

LargePrimeCli is a .NET 8 command-line toolkit for experimenting with large-prime generation and integer factorization. It implements the practical ideas described in Albert Meyburgh's paper, **Prime Candidate Sieving via Primitive Roots of Unity: A Spectral Reformulation of Wheel Factorization**.

- [Read the paper PDF](docs/paper/root_unity_wheel_paper_with_factorization.pdf)

## Paper summary

The paper shows that a standard prime wheel sieve has an exact roots-of-unity interpretation. For a wheel modulus

```math
M = \prod_{p \in B} p
```

built from small primes, the admissible residues are exactly the exponents `r` with `gcd(r, M) = 1`; equivalently, they are the exponents for which

```math
\zeta_M^r = e^{2\pi i r/M}
```

is a primitive `M`-th root of unity. A proposed "root collision" test therefore reduces to ordinary divisibility by stored primes, and the same wheel mask can be written spectrally with Ramanujan sums. In plain terms, this means the yes/no table used by a wheel sieve--keep this residue, reject that residue--can also be expressed as a finite Fourier-like sum of roots of unity. The Ramanujan-sum formula is another exact way to describe the same periodic pattern of allowed residues; it is mathematically useful for understanding the structure, but the code should still use ordinary modular arithmetic and cached wheel data rather than evaluating complex exponentials.

The paper also uses the same language to explain Pollard `p - 1`: a cached smooth exponent/root schedule can force a residue to collapse to the identity modulo one hidden prime factor, revealing a non-trivial factor by `gcd(a^M - 1, n)`.

The computational conclusion is intentionally conservative: the root-of-unity view is a useful design and explanatory model, but the fast implementation is conventional cached residue/gap wheels, small-prime filtering, Pollard `p - 1`, Pollard rho, Miller-Rabin probable-prime tests, and optional primality-proof layers such as Pocklington-style certificates.

## Project features

- Generate large probable primes of a requested bit length. These are probable primes, not proof-carrying primes.
- Build reusable prime caches with a segmented sieve.
- Factor integers using:
  - cached small-prime trial division,
  - optional large-prime cache trial division,
  - a cached Pollard `p - 1` prime-power/root schedule with streamed segmented-sieve stage 2,
  - Pollard rho fallback.
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

Run the built-in deterministic regression suite, including Brent rho semiprimes of varying sizes and repeated-factor cases:

```bash
dotnet run -- self-test
```
