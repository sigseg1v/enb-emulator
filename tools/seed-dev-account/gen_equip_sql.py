#!/usr/bin/env python3
# SPDX-License-Identifier: CC-BY-NC-SA-3.0
# Part of the Earth & Beyond emulator preservation project.
# License: LICENSES/enb-emulator
"""
Emit a net7 (content DB) query that resolves, per class_index, the best
level-appropriate gear the class can actually equip in each of the four
core equipment slots:

  slot 0  Shield   (sub_category 122, item_shield)
  slot 1  Reactor  (sub_category 120, item_reactor)
  slot 2  Engine   (sub_category 121, item_engine)
  slot 3  Weapon   (sub_category 100, item_beam -- a beam weapon, so it
                    needs no ammo to fire; the other launcher types would
                    leave the slot dead without also seeding ammo)

This is the gear half of `just seed-dev-account`. The level/credit/skill
bump (gen_bump_sql.py) leaves a character flying the tier-1 starting gear
the server laid down in ReInitializeSavedData. This resolves a genuinely
*equippable* upgrade per slot, matching exactly the gates the server's
Equipable::CanEquip() actually enforces -- nothing weaker, nothing the
real client would reject:

  * sub_category must match the slot (checked above);
  * the equip skill level must be >= the item's tech level
    (item_base.level). The bump sets every class skill to its per-class
    max from Skills.xml, so we cap item.level at that same per-class max,
    parsed here from the SAME Skills.xml -- never offer gear the maxed
    skill still could not equip;
  * rest_race must not restrict the class's race  (bit race  set => no);
  * rest_prof must not restrict the class's profession (bit prof set => no).

Crucially we do NOT gate on item_other_req (overall/combat/explore/trade
level requirements) or lore: the server never loads item_other_req and
hard-wires the lore field to 0 (see ItemBaseSQL.cpp -- ItemFields[17]=0,
item_other_req is queried nowhere), so those CanEquip branches are dead on
this server. Adding them here would diverge from the server's real gate.

The output is consumed by seed-dev-account.sh, which is on the net7_USER
connection and so cannot join these content tables (the two DBs are
isolated -- a cross-DB join in one connection fails). This query runs on
net7, prints `class_index|slot|item_id`, and the shell applies the rows to
avatar_equipment in net7_user keyed by each avatar's class_index.

class_index convention (StaticData.h): race*3 + prof, with
race 0=Terran 1=Jenquai 2=Progen, prof 0=Warrior 1=Trader 2=Explorer.
"""
import sys
import xml.etree.ElementTree as ET

# Skills.xml class element name -> class_index (SkillParser.cpp dispatch order)
ELEM_TO_CI = {
    "Enforcer": 0, "Tradesman": 1, "Scout": 2,
    "Defender": 3, "Seeker": 4, "Explorer": 5,
    "Warrior": 6, "Sentinel": 7, "Privateer": 8,
}

# Skill IDs (PlayerSkills.h) for the four core equipment slots.
SKILL_BEAM_WEAPON = 1
SKILL_ENGINE_TECH = 20
SKILL_REACTOR_TECH = 45
SKILL_SHIELD_TECH = 55

# slot -> (sub_category, restriction table, gating skill id)
SLOTS = [
    (0, 122, "item_shield",  SKILL_SHIELD_TECH),
    (1, 120, "item_reactor", SKILL_REACTOR_TECH),
    (2, 121, "item_engine",  SKILL_ENGINE_TECH),
    (3, 100, "item_beam",    SKILL_BEAM_WEAPON),
]


def load_skill_caps(skills_xml_path):
    """class_index -> {skill_id: max_rank} from Skills.xml."""
    raw = open(skills_xml_path, encoding="latin-1").read()
    root = ET.fromstring("<root>" + raw + "</root>")
    caps = {ci: {} for ci in range(9)}
    for sk in root.findall("Skill"):
        sid = int(sk.attrib["ID"])
        for child in sk:
            ci = ELEM_TO_CI.get(child.tag)
            if ci is None:
                continue
            mx = int(child.attrib.get("Max", "0"))
            if mx > 0:
                caps[ci][sid] = mx
    return caps


def main():
    if len(sys.argv) < 2:
        sys.exit("usage: gen_equip_sql.py <skills.xml path>")
    caps = load_skill_caps(sys.argv[1])

    selects = []
    for ci in range(9):
        race = ci // 3
        prof = ci % 3
        race_bit = 1 << race
        prof_bit = 1 << prof
        for slot, sub, tbl, skill_id in SLOTS:
            cap = caps[ci].get(skill_id, 0)
            if cap <= 0:
                # class cannot train this slot's skill at all -- leave the
                # server's starting item in place rather than force a piece
                # the skill check would reject.
                continue
            # Highest tech level the maxed skill can equip, best (highest id)
            # within that tier for determinism. Restrictions are bitmasks:
            # a SET bit for this race/prof means "restricted", so require 0.
            # Parenthesised so each UNION ALL branch carries its own
            # ORDER BY / LIMIT (a bare ORDER BY in a union branch is illegal).
            selects.append(
                "(SELECT %d AS class_index, %d AS slot, b.id AS item_id\n"
                "   FROM item_base b JOIN %s t ON t.item_id = b.id\n"
                "  WHERE b.sub_category = %d AND b.level <= %d\n"
                "    AND (t.rest_race & %d) = 0 AND (t.rest_prof & %d) = 0\n"
                "  ORDER BY b.level DESC, b.id DESC\n"
                "  LIMIT 1)"
                % (ci, slot, tbl, sub, cap, race_bit, prof_bit)
            )

    print("\nUNION ALL\n".join(selects) + ";")


if __name__ == "__main__":
    main()
