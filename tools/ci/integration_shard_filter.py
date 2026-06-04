#!/usr/bin/env python3
"""Deterministically partition the CliClient integration suite into N shards.

The CI used to run all ~355 integration tests in one serial job (one shared
docker-compose stack, single-client proxy -> the suite cannot parallelise
in-process). That job kept timing out. We instead run several *parallel* CI
jobs, each with its own stack, each executing a disjoint slice of the suite.

The slice is computed at runtime from `dotnet test --list-tests` output, NOT
from a hand-maintained list of test names. That is the whole point: a newly
added test is discovered automatically and lands in exactly one shard, so
nobody can forget to "add it to CI". `--check` proves the union of all shards
covers every discovered test exactly once.

Granularity is the test *method*, not the class: one class
(`SectorChatTests`) holds ~half the suite as ~176 separate [Fact]s, so a
class-level split would dump that whole half onto one shard and defeat the
purpose. Splitting by method balances the shards (greedy
longest-processing-time: heaviest methods placed first onto the lightest
shard). Every shard runs the identical algorithm over the identical
discovered list, so their assignments are consistent (disjoint and complete)
without any cross-job coordination.

Match safety (no test double-runs, none dropped):
  * a plain [Fact] (no theory args) is selected with an EXACT
    `FullyQualifiedName=...Class.Method` term -- cannot prefix-collide with a
    sibling method whose name merely starts the same.
  * a [Theory] (whose cases discover as `...Method(args)`) is selected with a
    contains term `FullyQualifiedName~...Class.Method`, because the case
    discriminator `(args)` makes an exact `=` term miss every case. We cannot
    anchor with the trailing '(' -- VSTest treats '(' as a filter grouping
    operator and a literal one breaks the whole expression -- so `--check`
    instead proves no method FQN is a prefix of another, which is the only way
    a contains term could leak into a neighbouring shard.

Usage:
    # emit the VSTest --filter expression for one shard
    integration_shard_filter.py --list-file tests.txt --shards 3 --shard 2

    # verify the partition covers every test exactly once (CI guard)
    integration_shard_filter.py --list-file tests.txt --shards 3 --check
"""
import argparse
import sys

ROOT = "N7.CliClient.IntegrationTests."


def parse_methods(list_file):
    """Return {method_fqn: {"count": int, "theory": bool}} from
    `dotnet test --list-tests` output.

    Each test line is an indented fully-qualified name, optionally followed by
    theory arguments in parens:
        N7.CliClient.IntegrationTests.Opcodes.FooTests.Bar
        N7.CliClient.IntegrationTests.Smoke.Baz.IsCitext(table: "accounts")
    We strip the argument list at the first '(' to recover the method FQN, and
    remember whether any case carried args (i.e. the method is a [Theory]).
    """
    methods = {}
    with open(list_file, encoding="utf-8", errors="replace") as fh:
        for raw in fh:
            line = raw.strip()
            # Only lines that are one of our test FQNs. The header
            # ("The following Tests are available:"), the VSTest banner, and
            # any build chatter do not start with the suite's root namespace.
            if not line.startswith(ROOT):
                continue
            has_args = "(" in line
            method = line.split("(", 1)[0].strip()
            if "." not in method:
                continue
            m = methods.setdefault(method, {"count": 0, "theory": False})
            m["count"] += 1
            m["theory"] = m["theory"] or has_args
    return methods


def assign(methods, shards):
    """Greedy LPT: return (buckets, loads) where buckets[i] is the list of
    method FQNs on shard i (0-based). Deterministic for a fixed input.
    """
    # Sort by (count desc, name) so ties break identically in every job.
    ordered = sorted(methods.items(), key=lambda kv: (-kv[1]["count"], kv[0]))
    buckets = [[] for _ in range(shards)]
    loads = [0] * shards
    for method, info in ordered:
        i = min(range(shards), key=lambda s: (loads[s], s))
        buckets[i].append(method)
        loads[i] += info["count"]
    return buckets, loads


def filter_term(method, theory):
    if theory:
        # Contains: matches every `Method(args)` case. Safe only because
        # --check proves this method FQN is not a prefix of another's.
        return f"FullyQualifiedName~{method}"
    return f"FullyQualifiedName={method}"


def filter_expr(methods, bucket):
    return "|".join(
        filter_term(m, methods[m]["theory"]) for m in sorted(bucket)
    )


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("--list-file", required=True,
                    help="captured output of `dotnet test --list-tests`")
    ap.add_argument("--shards", type=int, required=True)
    ap.add_argument("--shard", type=int,
                    help="1-based shard index to emit a --filter for")
    ap.add_argument("--check", action="store_true",
                    help="verify the partition covers every test exactly once")
    args = ap.parse_args(argv)

    if args.shards < 1:
        ap.error("--shards must be >= 1")

    methods = parse_methods(args.list_file)
    if not methods:
        print("ERROR: discovered 0 tests -- did the build/list step run?",
              file=sys.stderr)
        return 2

    buckets, loads = assign(methods, args.shards)

    if args.check:
        seen = {}
        for i, b in enumerate(buckets):
            for method in b:
                if method in seen:
                    print(f"ERROR: {method} assigned to shards "
                          f"{seen[method]+1} and {i+1}", file=sys.stderr)
                    return 1
                seen[method] = i
        missing = set(methods) - set(seen)
        if missing:
            print(f"ERROR: {len(missing)} methods unassigned: "
                  f"{sorted(missing)}", file=sys.stderr)
            return 1
        # A theory method's term matches by substring (contains). Guard
        # against the pathological case where a theory method's FQN is a
        # prefix of a DIFFERENT method's FQN -- that is the only way its
        # contains-term could pull a neighbouring shard's test in. (Fact
        # terms are exact `=`, so they never overlap.)
        names = sorted(methods)
        for a in names:
            if not methods[a]["theory"]:
                continue
            for b in names:
                if b != a and b.startswith(a):
                    print(f"ERROR: theory method {a} is a prefix of {b}; "
                          f"its contains-filter would overlap shards",
                          file=sys.stderr)
                    return 1
        total = sum(m["count"] for m in methods.values())
        print(f"OK: {len(methods)} methods / {total} tests across "
              f"{args.shards} shards, covered exactly once.")
        for i in range(args.shards):
            print(f"  shard {i+1}: {len(buckets[i])} methods, "
                  f"{loads[i]} tests")
        return 0

    if args.shard is None:
        ap.error("--shard is required unless --check is given")
    if not 1 <= args.shard <= args.shards:
        ap.error(f"--shard must be in 1..{args.shards}")

    print(filter_expr(methods, buckets[args.shard - 1]))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
