#!/usr/bin/env python3
# SPDX-License-Identifier: CC-BY-NC-SA-3.0
# Part of the Earth & Beyond emulator preservation project.
# License: LICENSES/enb-emulator
"""
Emit the "bump to a level-75 dev character" SQL for every avatar owned by a
named account.

This is the second half of `just seed-dev-account`. The first half drives the
CLI to `create` + `enter` each character, which lets the SERVER run its own
ReInitializeSavedData() path -- that is what lays down the starting gear
(shield / reactor / engine / weapon), the home-station position, the base
skills, faction state and the avatar_level_info row. We deliberately do NOT
fabricate any of that in SQL: inventing starting equipment / colours / faction
rows blind would be garbage data, and the server already knows how to do it
correctly.

What this script bumps, against the rows the server just created, is only the
plain stored values the server trusts verbatim on a normal ReloadSavedData:

  * avatar_info.combat/explore/trade  -> 25 / 25 / 25  (the integer levels; the
    double-ntohl on the load path cancels, so the raw column IS the level, so
    total level = 75)
  * avatar_level_info.credits          -> 10,000,000
  * avatar_level_info hull/slots/cargo -> the values the server itself would
    have written at hull-upgrade level 4, derived from server/src/StaticData.h
    (no invention -- this is the server's own progression model)
  * avatar_skill_levels                -> every skill the class can train, at
    that class's max rank, parsed from server/data/Skills.xml. The server's
    login clamp (PlayerSaves.cpp) caps each skill to the per-class max anyway,
    so anything off-class is harmless, but we only emit on-class skills to keep
    the table clean.

class_index convention (StaticData.h): race*3 + prof, with
race 0=Terran 1=Jenquai 2=Progen, prof 0=Warrior 1=Trader 2=Explorer.
"""
import sys
import xml.etree.ElementTree as ET

# Hull-upgrade level we bring dev characters to. 0..6; 4 is a sane "mid-high"
# tier for a level-75 character. Slots / hull / cargo below are all derived
# from this so the row stays self-consistent with the server's own model.
HULL_UPGRADE = 4

# --- per-class static tables, verbatim from server/src/StaticData.h ---------
# Order: TW TT TE JW JT JE PW PT PE  (== class_index 0..8)
MAX_WEAPON_SLOTS = [5, 4, 4, 5, 4, 3, 6, 5, 4]
MAX_DEVICE_SLOTS = [4, 5, 5, 4, 5, 6, 3, 4, 5]
BASE_CARGO       = [23, 28, 23, 18, 28, 18, 18, 28, 18]
# 7 entries per class: cumulative slots granted at hull-upgrade level 0..6
WEAPON_TABLE = [
    2, 0, 1, 0, 1, 0, 1,   # TW
    1, 0, 1, 0, 1, 0, 1,   # TT
    1, 0, 1, 0, 1, 0, 1,   # TE
    2, 0, 1, 0, 1, 0, 1,   # JW
    1, 0, 1, 0, 1, 0, 1,   # JT
    1, 0, 1, 0, 0, 1, 0,   # JE
    2, 1, 0, 1, 1, 0, 1,   # PW
    2, 0, 1, 0, 1, 1, 0,   # PT
    2, 0, 1, 0, 0, 1, 0,   # PE
]
DEVICE_TABLE = [
    1, 1, 0, 1, 0, 1, 0,   # TW
    2, 1, 0, 1, 0, 1, 0,   # TT
    2, 1, 0, 1, 0, 1, 0,   # TE
    1, 1, 0, 1, 0, 1, 0,   # JW
    2, 1, 0, 1, 0, 1, 0,   # JT
    2, 1, 0, 1, 1, 0, 1,   # JE
    1, 0, 1, 0, 0, 1, 0,   # PW
    1, 1, 0, 1, 0, 0, 1,   # PT
    1, 1, 0, 1, 1, 0, 1,   # PE
]
HULL_TABLE = [
    18, 70, 280, 1100, 4500, 18000, 72000,   # TW
    13, 55, 210, 850, 3400, 13400, 56000,    # TT
    12, 50, 190, 750, 3000, 12000, 48000,    # TE
    16, 65, 260, 1000, 4100, 16500, 65500,   # JW
    12, 50, 190, 770, 3100, 12500, 49000,    # JT
    11, 45, 170, 680, 2700, 11000, 44000,    # JE
    20, 80, 320, 1300, 5100, 20500, 82000,   # PW
    15, 60, 240, 960, 3900, 15500, 61000,    # PT
    13, 50, 210, 850, 3400, 13700, 55000,    # PE
]

# Skills.xml class element name -> class_index (SkillParser.cpp dispatch order)
ELEM_TO_CI = {
    "Enforcer": 0, "Tradesman": 1, "Scout": 2,
    "Defender": 3, "Seeker": 4, "Explorer": 5,
    "Warrior": 6, "Sentinel": 7, "Privateer": 8,
}


def ship_params(ci):
    """Return (hull_points, weapon_slots, device_slots, cargo) at HULL_UPGRADE."""
    base = ci * 7
    wslots = sum(WEAPON_TABLE[base:base + HULL_UPGRADE + 1])
    dslots = sum(DEVICE_TABLE[base:base + HULL_UPGRADE + 1])
    hull = HULL_TABLE[base + HULL_UPGRADE]
    cargo = BASE_CARGO[ci] + 2 * HULL_UPGRADE
    # never grant fewer slots than a fresh character starts with
    return hull, max(wslots, 1), max(dslots, 1), cargo


def load_skillmap(skills_xml_path):
    """class_index -> {skill_id: max_rank} from Skills.xml."""
    raw = open(skills_xml_path, encoding="latin-1").read()
    root = ET.fromstring("<root>" + raw + "</root>")
    per = {ci: {} for ci in range(9)}
    for sk in root.findall("Skill"):
        sid = int(sk.attrib["ID"])
        for child in sk:
            ci = ELEM_TO_CI.get(child.tag)
            if ci is None:
                continue
            mx = int(child.attrib.get("Max", "0"))
            if mx > 0:
                per[ci][sid] = mx
    return per


def main():
    if len(sys.argv) < 2:
        sys.exit("usage: gen_bump_sql.py <skills.xml path>")
    skillmap = load_skillmap(sys.argv[1])

    out = []
    out.append("-- generated by tools/seed-dev-account/gen_bump_sql.py")
    out.append("-- bumps every avatar of :'acctname' to a level-75 dev character")
    out.append("BEGIN;")
    out.append("")
    out.append("-- resolve the account's avatars once")
    out.append(
        "CREATE TEMP TABLE _devseed_av ON COMMIT DROP AS\n"
        "  SELECT ai.avatar_id, (ad.race*3 + ad.prof) AS ci\n"
        "  FROM avatar_info ai JOIN avatar_data ad ON ad.avatar_id = ai.avatar_id\n"
        "  WHERE ai.account_id = (SELECT id FROM accounts WHERE username = :'acctname');"
    )
    out.append("")
    out.append("-- 1. integer levels: combat/explore/trade = 25 each (total 75)")
    out.append(
        "UPDATE avatar_info SET combat = 25, explore = 25, trade = 25\n"
        " WHERE avatar_id IN (SELECT avatar_id FROM _devseed_av);"
    )
    out.append("")
    out.append("-- 2. credits + ship capability (hull-upgrade %d, from StaticData.h)" % HULL_UPGRADE)
    sp_rows = []
    for ci in range(9):
        hull, w, d, cargo = ship_params(ci)
        sp_rows.append("(%d,%d,%d,%d,%d)" % (ci, hull, w, d, cargo))
    out.append(
        "UPDATE avatar_level_info li SET\n"
        "    credits = 10000000,\n"
        "    hull_upgrade_level = %d,\n"
        "    max_hull_points = sp.hull_pts,\n"
        "    hull_points = sp.hull_pts,\n"
        "    cargo_space = sp.cargo,\n"
        "    weapon_slots = sp.wslots,\n"
        "    device_slots = sp.dslots\n"
        "  FROM _devseed_av av,\n"
        "       (VALUES %s) AS sp(ci, hull_pts, wslots, dslots, cargo)\n"
        "  WHERE li.avatar_id = av.avatar_id AND av.ci = sp.ci;"
        % (HULL_UPGRADE, ", ".join(sp_rows))
    )
    out.append("")
    out.append("-- 3. all class-appropriate skills at their per-class max rank")
    skill_rows = []
    for ci in range(9):
        for sid, mx in sorted(skillmap[ci].items()):
            skill_rows.append("(%d,%d,%d)" % (ci, sid, mx))
    out.append(
        "INSERT INTO avatar_skill_levels (avatar_id, skill_id, skill_level)\n"
        "SELECT av.avatar_id, sk.skill_id, sk.rank\n"
        "  FROM _devseed_av av\n"
        "  JOIN (VALUES %s) AS sk(ci, skill_id, rank) ON av.ci = sk.ci\n"
        "ON CONFLICT (avatar_id, skill_id) DO UPDATE SET skill_level = EXCLUDED.skill_level;"
        % ", ".join(skill_rows)
    )
    out.append("")
    out.append("COMMIT;")
    out.append("")
    print("\n".join(out))


if __name__ == "__main__":
    main()
