# LargePrimeCli

Command-line C#/.NET application for generating large primes using the supplied `LargePrimeSearcher` implementation.

## Requirements

- .NET 8 SDK

## Generate primes

```bash
dotnet run -- --bits 256
```

By default the app looks for cached inputs at:

- `.prime-cache/small-primes.txt`
- `.prime-cache/large-primes.txt`

If no small-prime cache exists, it falls back to generating small primes up to `--small-prime-limit` in memory.

## Create the prime cache

Use the separate `cache` utility command to enumerate primes from `1` to `N` with a segmented sieve and write them into the cache files used by future runs. It prints each discovered verified prime to stdout as it is found. On exit, it reports how far it processed and how long it ran:

```bash
dotnet run -- cache --max 1000000
```

Custom cache output paths:

```bash
dotnet run -- cache --max 1000000 \
  --small-out ./cache/small-primes.txt \
  --large-out ./cache/large-primes.txt
```

Then use those files for generation. On exit, generation reports elapsed time and the largest prime generated:

```bash
dotnet run -- --bits 256 \
  --small-primes-file ./cache/small-primes.txt \
  --large-primes-file ./cache/large-primes.txt
```

## Factor a number

The factor command uses the same cached prime inputs for trial division, then tries a roots-of-unity style Pollard `p - 1` search, and falls back to Pollard rho.

```powershell
dotnet .\publish\LargePrimeCli.dll factor 8051
```

Output:

```text
83
97
```

The `p - 1` step looks for a collision:

```text
gcd(a^M - 1, n)
```

If a prime factor `p` of `n` has `p - 1` composed of small primes under the bound, then `a^M ≡ 1 mod p`, creating a root-of-unity collision that reveals `p` via the gcd.

The modular residues themselves depend on the number being factored, so they cannot be reused across different inputs. The app does cache the reusable part: the Pollard `p - 1` prime-power/root schedule. By default this is saved at:

```text
.prime-cache/pminus1-powers-<bound>.txt
```

Options:

```text
    --pminus1-bound <n>        Smoothness bound for Pollard p-1/root collision search (default: 100000)
-s, --small-prime-limit <n>    Fallback trial-division prime limit when no cache exists (default: 100000)
    --small-primes-file <path> Small prime cache file (default: .prime-cache/small-primes.txt)
    --large-primes-file <path> Large prime cache file (default: .prime-cache/large-primes.txt)
    --root-schedule-file <path> Cache file for reusable Pollard p-1 prime-power/root schedule
    --use-large-prime-cache    Trial-divide by large-primes cache too; off by default for huge inputs
-q, --quiet                    Only print factors to stdout
-h, --help                     Show help
```

## Generate command options

```text
-b, --bits <n>                 Prime bit length (default: 128, min: 16)
-r, --rounds <n>               Miller-Rabin rounds (default: 64)
-s, --small-prime-limit <n>    Fallback small prime generation limit when no cache exists (default: 10000)
-c, --count <n>                Number of primes to generate (default: 1)
    --small-primes-file <path> Small prime cache file (default: .prime-cache/small-primes.txt)
    --large-primes-file <path> Large prime cache file (default: .prime-cache/large-primes.txt)
-f, --format <decimal|hex>     Output format (default: decimal)
-q, --quiet                    Only print generated prime values to stdout
-h, --help                     Show help
```

## Cache command options

```text
-n, --max <N>              Generate every prime <= N
    --small-out <path>     Output file for int-sized primes (default: .prime-cache/small-primes.txt)
    --large-out <path>     Output file for primes > int.MaxValue (default: .prime-cache/large-primes.txt)
    --segment-size <n>     Segmented sieve block size (default: 1000000)
    --no-emit-primes       Do not print each discovered verified prime to stdout; instead prints speed/memory stats every 5 minutes
-q, --quiet                Reduce progress output; primes still print unless --no-emit-primes is used
-h, --help                 Show help
```

Build a release publish folder:

```bash
dotnet publish -c Release -o ./publish
dotnet ./publish/LargePrimeCli.dll --bits 256
```
