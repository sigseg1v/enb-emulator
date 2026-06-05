#!/usr/bin/env python3
# SPDX-License-Identifier: CC-BY-NC-SA-3.0
# Part of the Earth & Beyond emulator preservation project.
# License: LICENSES/enb-emulator
"""
Generate an idempotent SQL seed that fills in the item_effects / item_effect_base
/ item_effect_container gap between the external item dataset and our runtime DB.

WHAT THIS DOES, and the honesty boundary
----------------------------------------
Our runtime DB already carries 5063 items and ~3843 item->effect links. The
external dataset (item-info/*.jsonl) restates almost all of that, but lists a
set of (item, effect) pairs we are missing. Each external effect entry gives a
plain-text effect name + type (which maps EXACTLY onto item_effect_base.Description
== "<name> (<type>)") and a player-facing magnitude (`value`), plus sometimes a
duration. It does NOT carry the server's internal Var2/Var3 magnitudes for the
effects that use a second/third variable.

So every emitted row is tagged with a confidence:

  CONFIRMED  - the value is taken directly from the source and matches the
               server's storage convention, proven against existing rows:
                 * Var1Data = abs(value)                 (single-stat effects)
                 * multi-stat effects: each external sub-entry, in stat order,
                   -> abs(value) into the matching Var slot (Shunt-to-Reactor etc.)
                 * activated duration -> the first NO_STAT slot, from `duration`
  GENERATED  - the slot is a value the source does NOT contain; we fill it
               DETERMINISTICALLY and IN A VALID RANGE: the per-(effect,slot)
               median of existing rows at the same item level, clamped to that
               effect+slot's observed [min,max]. Unknown activated durations
               fall back to 180 s (3 minutes).

The output is therefore NOT capture-faithful for the GENERATED slots. It is kept
in its own seed file and every generated slot is auditable from the sidecar
report (--report) so it can never be mistaken for primary-source data.

Inputs (all regenerable from the live DB; see the dump step in the justfile /
docs). Pass --learn-dir pointing at a directory holding:
  base_meta.tsv       Description, EffectID, EffectType, Var1Stat, Var2Stat, Var3Stat
  learn_rows.tsv      ItemID, level, Description, EffectType, Var1Data, Var2Data, Var3Data
  existing_links.tsv  ItemID, Description           (already-present links, for dedup)
  has_container.tsv   ItemID                        (items that already have a container)
  item_base.tsv       id, level, name              (id resolution + level for fab)
and --item-info pointing at the external item-info directory.
"""
import argparse
import glob
import json
import os
import statistics
import sys


# --- Stat wiring for minted bases -----------------------------------------
# Effect keys absent from item_effect_base get a base minted for them. Most are
# left as NO_STAT placeholders (inert) and flagged for manual review (--sus-out).
# The few below have an UNAMBIGUOUS existing analog -- same stat semantics AND
# the same EffectType direction (an activated->activated or equip->equip "Item C"
# / "Item P" sibling) -- so we clone that analog's proven mechanical columns
# instead of guessing. Each value is:
#   (Var1Stat, Var1Type, Var2Stat, Var2Type, Buff_Name, Var1_mod, Var2_mod, VisualEffect)
# Source analog cited per line. Anything with a direction/type ambiguity is
# deliberately NOT here -- it goes to the SUS list for owner approval instead.
MINTED_STAT_WIRING = {
    # clone of "Increases Reactor capacity Item C"
    "Increase Reactor Capacity Self (Activated)":
        ("STAT_ENERGY", 1, "NO_STAT", 5, "Reactor_Boost", 1.0, 1.33, 0),
    # clone of "Scan Range Boost Item C"
    "Increase Scan Range (Activated)":
        ("STAT_SCAN_RANGE", 1, "NO_STAT", 0, "BOOST Increase Scan Range", 1.33, 1.0, 462),
    # clone of "Reduces Projectile Energy Item P" (conservation == uses less energy)
    "Projectile Energy Conservation (Equip)":
        ("STAT_PROJECTILE_ENERGY", 4, "NO_STAT", 0, "BUFF_NONE", 1.0, 1.0, 0),
    # clone of "See Cloaked Item C"
    "See Cloaked (Activated)":
        ("STAT_SEE_CLOAKED", 5, "NO_STAT", 0, "See Cloaked", 1.0, 1.0, 0),
    # clone of "Worsen Beam Handling Item C"
    "Worsen Beam Handling Self (Activated)":
        ("STAT_BEAM_ACCURACY", 3, "STAT_BEAM_DAMAGE", 4, "Lower Beam Weapon Skill", 1.33, 1.33, 447),
    # clone of "Worsen Missile Handling Item C"
    "Worsen Missile Handling Self (Activated)":
        ("STAT_MISSILE_ACCURACY", 3, "STAT_MISSILE_DAMAGE", 4, "Lower Missile Weapon Skill", 1.33, 1.33, 447),
}

# Suggested-but-unconfirmed stat for the SUS list. NOT applied -- shown to the
# owner so a manual fix is a confirm/edit rather than a from-scratch lookup.
# Reason states why it is not auto-wired.
SUS_SUGGESTIONS = {
    "Improved Critical Targeting Self (Activated)":
        ("STAT_CRITICAL_HIT", "stat clear, but only an EQUIP analog exists; this is activated/self"),
    "Improved Jumpstart (Equip)":
        ("SKILL_JUMPSTART", "skill exists but NO existing base uses it -- no analog to clone"),
    "Improved Navigation (Equip)":
        ("SKILL_NAVIGATE", "ambiguous: SKILL_NAVIGATE vs STAT_TURN_RATE vs STAT_WARP"),
    "Increase Engine Thrust (Activated)":
        ("STAT_IMPULSE", "ambiguous: STAT_IMPULSE (accel) vs STAT_ENGINE_TOP_SPEED"),
    "Equipment Damage Magnification - Reactor Self (Activated)":
        ("STAT_EQUIPMENT_DAMAGE_CONTROL_REACTOR", "only an EQUIP analog exists; this is activated/self"),
    "Lower Shield Capacity Self (Activated)":
        ("STAT_SHIELD", "stat clear (reduce), but no activated/self analog to clone the type from"),
    "Weapon Engineering (Equip)":
        ("SKILL_BUILD_WEAPONS", "ambiguous: SKILL_BUILD_WEAPONS vs STAT_EQUIPMENT_ENGINEERING"),
    "Recalibrate Shielding System (Activated)":
        ("STAT_SHIELD_RECHARGE", "ambiguous: STAT_SHIELD_RECHARGE vs SKILL_RECHARGE_SHIELDS"),
    "Dread (Activated)":
        ("SKILL_MENACE", "ambiguous fear/aggro: SKILL_MENACE vs SKILL_ENRAGE"),
    "One Time Wormhole (Activated)":
        ("SKILL_WORMHOLE", "likely a scripted create-wormhole device, not a stat buff"),
    "Worsen Scan Range Increase Ship Signature (Activated)":
        ("STAT_SCAN_RANGE + STAT_SIGNATURE", "COMPOUND name: two stats; slot order unknown"),
}


def load_tsv(path):
    rows = []
    with open(path, encoding="utf-8") as fh:
        for line in fh:
            line = line.rstrip("\n")
            if line == "":
                continue
            rows.append(line.split("\t"))
    return rows


def fnum(s):
    """Parse a numeric string; return None if not numeric."""
    if s is None:
        return None
    s = str(s).strip()
    if s == "" or s == r"\N":
        return None
    try:
        return float(s)
    except ValueError:
        return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--item-info", required=True, help="external item-info/ directory")
    ap.add_argument("--learn-dir", required=True, help="dir with the DB dump TSVs")
    ap.add_argument("--report", help="write a per-row CONFIRMED/GENERATED report here")
    ap.add_argument("--sus-out", help="write the minted bases needing owner review here")
    args = ap.parse_args()

    ld = args.learn_dir
    base_meta = load_tsv(os.path.join(ld, "base_meta.tsv"))
    learn_rows = load_tsv(os.path.join(ld, "learn_rows.tsv"))
    existing = load_tsv(os.path.join(ld, "existing_links.tsv"))
    has_container = {int(r[0]) for r in load_tsv(os.path.join(ld, "has_container.tsv"))}
    item_base = load_tsv(os.path.join(ld, "item_base.tsv"))

    # --- base catalog: Description -> (EffectID, EffectType, [stat0,stat1,stat2]) ---
    base_by_descr = {}
    max_effect_id = 0
    for d, eid, etype, s1, s2, s3 in base_meta:
        eid = int(eid)
        base_by_descr[d] = {"eid": eid, "etype": int(etype), "stats": [s1, s2, s3]}
        max_effect_id = max(max_effect_id, eid)

    # --- id resolution + level ---
    id_level = {}
    name_to_id = {}
    for r in item_base:
        iid, lvl, name = int(r[0]), int(r[1]), r[2]
        id_level[iid] = lvl
        name_to_id.setdefault(name, iid)

    # --- already-present (ItemID, Description) links, for dedup ---
    have_link = {(int(r[0]), r[1]) for r in existing}

    # --- fabrication tables: (Description, slot) -> {level: [values]} plus min/max ---
    fab = {}        # (descr, slot) -> {level: [vals]}
    fab_range = {}  # (descr, slot) -> (min, max)
    for iid, lvl, descr, etype, v1, v2, v3 in learn_rows:
        lvl = int(lvl)
        for slot, raw in enumerate((v1, v2, v3)):
            val = fnum(raw)
            if val is None:
                continue
            fab.setdefault((descr, slot), {}).setdefault(lvl, []).append(val)
            lo, hi = fab_range.get((descr, slot), (val, val))
            fab_range[(descr, slot)] = (min(lo, val), max(hi, val))

    def fab_value(descr, slot, level):
        """Deterministic in-range fill: median at this level, else nearest level,
        clamped to the effect+slot's observed [min,max]. 0 if the slot is always 0."""
        table = fab.get((descr, slot))
        if not table:
            return 0.0
        if level in table:
            val = statistics.median(table[level])
        else:
            nearest = min(table.keys(), key=lambda L: (abs(L - level), L))
            val = statistics.median(table[nearest])
        lo, hi = fab_range[(descr, slot)]
        return max(lo, min(hi, val))

    # --- read the external item records ---
    info_files = sorted(glob.glob(os.path.join(args.item_info, "*.jsonl")))
    records = []
    for f in info_files:
        with open(f, encoding="utf-8") as fh:
            for line in fh:
                line = line.strip()
                if not line:
                    continue
                d = json.loads(line)
                records.append(d)

    def resolve_iid(d):
        iid = d.get("item_id")
        if iid is None:
            iid = name_to_id.get(d.get("name"))
        return None if iid is None else int(iid)

    # Mint a base ONLY for effect keys that have no base AND will actually
    # receive at least one new link -- otherwise we would create an orphan base
    # no item references (the "Improved Enrage" bug). Deterministic, sorted ids.
    mintable = set()
    for d in records:
        iid = resolve_iid(d)
        if iid is None:
            continue
        for e in d.get("effects") or []:
            nm, ty = e.get("name"), e.get("type")
            if not (nm and ty):
                continue
            key = f"{nm} ({ty})"
            if key not in base_by_descr and (iid, key) not in have_link:
                mintable.add(key)

    new_bases = []  # (eid, etype, name, descr, wiring|None)
    next_eid = max_effect_id + 1
    for key in sorted(mintable):
        # type from the trailing "(...)"; Equip -> 0, Activated/Instant -> 1
        etype = 0 if key.endswith("(Equip)") else 1
        name = key.rsplit(" (", 1)[0]
        wiring = MINTED_STAT_WIRING.get(key)
        stats = (["NO_STAT", "NO_STAT", "NO_STAT"] if not wiring
                 else [wiring[0], wiring[2], "NO_STAT"])
        base_by_descr[key] = {"eid": next_eid, "etype": etype,
                              "stats": stats, "minted": True}
        new_bases.append((next_eid, etype, name, key, wiring))
        next_eid += 1

    # --- walk items, build the import set ---
    eff_rows = []        # (item_id, eid, v1, v2, v3)
    container_items = {}  # item_id -> etype_has_activated(bool)
    report = []
    skipped_no_id = []
    for d in records:
        iid = d.get("item_id")
        name = d.get("name")
        if iid is None:
            iid = name_to_id.get(name)
            if iid is None:
                skipped_no_id.append(name)
                continue
        iid = int(iid)
        level = id_level.get(iid, 1)

        # group this item's effect entries by effect key (descr)
        groups = {}
        for idx, e in enumerate(d.get("effects") or []):
            nm, ty = e.get("name"), e.get("type")
            if not (nm and ty):
                continue
            key = f"{nm} ({ty})"
            groups.setdefault(key, []).append((idx, e))

        for key, entries in groups.items():
            base = base_by_descr.get(key)
            if base is None:
                continue
            if (iid, key) in have_link:
                continue  # already linked -- never duplicate
            entries.sort(key=lambda x: x[0])
            stats = base["stats"]
            etype = base["etype"]
            real_idx = [i for i in range(3) if stats[i] != "NO_STAT"]
            var = [None, None, None]
            conf = ["", "", ""]

            # real-stat slots, in order, from the sub-entries
            for k, slot_i in enumerate(real_idx):
                if k < len(entries):
                    v = fnum(entries[k][1].get("value"))
                    if v is not None:
                        var[slot_i] = abs(v)
                        conf[slot_i] = "confirmed"
            # single-stat base with a NO_STAT Var1 (minted bases): still take entry0
            if not real_idx and entries:
                v = fnum(entries[0][1].get("value"))
                if v is not None:
                    var[0] = abs(v)
                    conf[0] = "confirmed"

            # activated duration -> first free NO_STAT slot
            if etype == 1:
                dslot = len(real_idx)
                if dslot <= 2 and var[dslot] is None:
                    dur = None
                    for _, e in entries:
                        dur = fnum(e.get("duration"))
                        if dur is not None:
                            break
                    if dur is not None:
                        var[dslot] = dur
                        conf[dslot] = "confirmed"
                    else:
                        var[dslot] = 180.0  # 3-minute fallback
                        conf[dslot] = "generated:180"

            # remaining empty slots -> deterministic in-range fill
            for i in range(3):
                if var[i] is None:
                    m = fab_value(key, i, level)
                    var[i] = m
                    conf[i] = "generated" if m != 0 else "zero"

            eff_rows.append((iid, base["eid"], var[0], var[1], var[2]))
            container_items.setdefault(iid, False)
            if etype == 1:
                container_items[iid] = True
            report.append((iid, key, base["eid"], var, conf))

    # --- emit SQL ---
    out = sys.stdout
    out.write("-- generated by tools/dataimport-jsonl/gen_item_effects_seed.py\n")
    out.write("-- Fills the item_effects/item_effect_base/item_effect_container gap.\n")
    out.write("-- CONFIRMED slots come from the source; GENERATED slots are\n")
    out.write("-- deterministic in-range fills (see --report). Idempotent.\n")
    out.write("BEGIN;\n\n")

    # The schema bulk-loads these IDENTITY columns with explicit ids without
    # advancing their backing sequences (sync_sequences.sql normally fixes this,
    # but it runs AFTER the seeds). Our INSERTs below let ItemEffectID /
    # EffectContainerID auto-allocate, so without this they would start at 1 and
    # collide with existing rows. Make the seed self-contained.
    out.write(
        "-- advance identity sequences past existing data so auto-allocated ids do not collide\n"
        "SELECT setval(pg_get_serial_sequence('item_effects','ItemEffectID'),\n"
        '       GREATEST((SELECT COALESCE(MAX("ItemEffectID"),0) FROM item_effects),1));\n'
        "SELECT setval(pg_get_serial_sequence('item_effect_container','EffectContainerID'),\n"
        '       GREATEST((SELECT COALESCE(MAX("EffectContainerID"),0) FROM item_effect_container),1));\n'
        "SELECT setval(pg_get_serial_sequence('item_effect_base','EffectID'),\n"
        '       GREATEST((SELECT COALESCE(MAX("EffectID"),0) FROM item_effect_base),1));\n\n')

    if new_bases:
        n_wired = sum(1 for *_, w in new_bases if w)
        out.write("-- minted effect bases for effect keys absent from item_effect_base.\n")
        out.write("-- %d of %d carry a wired stat (cloned from a proven analog, see\n"
                  "-- MINTED_STAT_WIRING in the generator); the rest are NO_STAT\n"
                  "-- placeholders pending owner review (see the SUS list).\n"
                  % (n_wired, len(new_bases)))
        rows = []
        for eid, et, nm, descr, w in new_bases:
            if w:
                v1s, v1t, v2s, v2t, buff, v1m, v2m, vis = w
            else:
                v1s, v1t, v2s, v2t, buff, v1m, v2m, vis = \
                    "NO_STAT", 0, "NO_STAT", 0, "BUFF_NONE", 1.0, 1.0, -1
            rows.append("(%d,%d,%s,%s,%s,%s,%d,%s,%d,%s,%s,%s,%d)" % (
                eid, et, sql_str(nm), sql_str(descr), sql_str(descr),
                sql_str(v1s), v1t, sql_str(v2s), v2t, sql_str(buff),
                fmt(v1m), fmt(v2m), vis))
        out.write(
            'INSERT INTO item_effect_base\n'
            '  ("EffectID","EffectType","Name","Description","Tooltip",\n'
            '   "Var1Stat","Var1Type","Var2Stat","Var2Type","Buff_Name",\n'
            '   "Var1_mod","Var2_mod","VisualEffect")\n'
            "SELECT v.* FROM (VALUES\n  "
            + ",\n  ".join(rows) +
            "\n) AS v(eid,et,nm,descr,tip,v1s,v1t,v2s,v2t,buff,v1m,v2m,vis)\n"
            'WHERE NOT EXISTS (SELECT 1 FROM item_effect_base b WHERE b."EffectID"=v.eid);\n\n')

    if container_items:
        out.write("-- containers for imported items that lack one (EquipEffect flag 0x31;\n")
        out.write("-- activated items get median activation params: recharge 0, range 2500, energy 365)\n")
        cvals = []
        for iid in sorted(container_items):
            if iid in has_container:
                continue
            if container_items[iid]:
                cvals.append("(%d,0,2500,365)" % iid)   # has an activated effect
            else:
                cvals.append("(%d,0,0,0)" % iid)         # equip-only
        if cvals:
            out.write(
                'INSERT INTO item_effect_container ("ItemID","EquipEffect","RechargeTime","Unknown2","_Range","Unknown4","EnergyUse","Energy_mod")\n'
                "SELECT v.iid, decode('31','hex'), v.rt, 0, v.rng, 0, v.en, 1 FROM (VALUES\n  "
                + ",\n  ".join(cvals) +
                "\n) AS v(iid,rt,rng,en)\n"
                'WHERE NOT EXISTS (SELECT 1 FROM item_effect_container c WHERE c."ItemID"=v.iid)\n'
                "  AND EXISTS (SELECT 1 FROM item_base ib WHERE ib.id=v.iid);\n\n")

    out.write("-- the effect links themselves\n")
    evals = ",\n  ".join(
        "(%d,%d,%s,%s,%s)" % (iid, eid, fmt(v1), fmt(v2), fmt(v3))
        for iid, eid, v1, v2, v3 in eff_rows)
    out.write(
        'INSERT INTO item_effects ("ItemID","item_effect_base_id","Var1Data","Var2Data","Var3Data")\n'
        "SELECT v.iid,v.eid,v.v1,v.v2,v.v3 FROM (VALUES\n  "
        + evals +
        "\n) AS v(iid,eid,v1,v2,v3)\n"
        'WHERE NOT EXISTS (SELECT 1 FROM item_effects ie WHERE ie."ItemID"=v.iid AND ie."item_effect_base_id"=v.eid)\n'
        '  AND EXISTS (SELECT 1 FROM item_base ib WHERE ib.id=v.iid)\n'
        '  AND EXISTS (SELECT 1 FROM item_effect_base b WHERE b."EffectID"=v.eid);\n\n')

    out.write("COMMIT;\n")

    # --- report + summary to stderr ---
    if args.report:
        with open(args.report, "w", encoding="utf-8") as rf:
            rf.write("item_id\teffect_key\teid\tVar1\tVar2\tVar3\tconf1\tconf2\tconf3\n")
            for iid, key, eid, var, conf in report:
                rf.write("%d\t%s\t%d\t%s\t%s\t%s\t%s\t%s\t%s\n" % (
                    iid, key, eid, fmt(var[0]), fmt(var[1]), fmt(var[2]),
                    conf[0], conf[1], conf[2]))

    # --- SUS list: minted bases left as NO_STAT placeholders for owner review ---
    link_count = {}
    for _, eid, *_ in eff_rows:
        link_count[eid] = link_count.get(eid, 0) + 1
    sus = [(eid, et, nm, descr) for eid, et, nm, descr, w in new_bases if not w]
    if args.sus_out:
        with open(args.sus_out, "w", encoding="utf-8") as sf:
            sf.write("# Minted item_effect_base rows still set to NO_STAT (inert).\n")
            sf.write("# Each needs an owner decision: confirm/correct the suggested\n")
            sf.write("# stat, then add it to MINTED_STAT_WIRING and regenerate.\n")
            sf.write("# A blank suggestion means no candidate stat was found at all\n")
            sf.write("# (likely a scripted/cosmetic effect, not a stat modifier).\n")
            sf.write("EffectID\ttype\tname\tlinked_items\tsuggested_stat\treason\n")
            for eid, et, nm, descr in sus:
                sugg, reason = SUS_SUGGESTIONS.get(descr, ("", "no candidate stat found"))
                sf.write("%d\t%s\t%s\t%d\t%s\t%s\n" % (
                    eid, "Equip" if et == 0 else "Activated", nm,
                    link_count.get(eid, 0), sugg, reason))

    n_conf = sum(1 for _, _, _, _, conf in report for c in conf if c == "confirmed")
    n_gen = sum(1 for _, _, _, _, conf in report for c in conf if c.startswith("generated"))
    n_zero = sum(1 for _, _, _, _, conf in report for c in conf if c == "zero")
    n_wired = sum(1 for *_, w in new_bases if w)
    sys.stderr.write(
        "rows=%d  minted_bases=%d (wired=%d sus=%d)  containers=%d  no_id_unresolved=%d\n"
        "slots: confirmed=%d generated=%d zero=%d\n" % (
            len(eff_rows), len(new_bases), n_wired, len(sus),
            len(container_items), len(skipped_no_id), n_conf, n_gen, n_zero))


def sql_str(s):
    return "'" + str(s).replace("'", "''") + "'"


def fmt(x):
    """Compact numeric literal."""
    if x == int(x):
        return str(int(x))
    return repr(round(float(x), 4))


if __name__ == "__main__":
    main()
