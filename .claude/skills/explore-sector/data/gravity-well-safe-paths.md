# Gravity-well transit -- safe warp paths (owner-taught 2026-07-12)

Gravity wells terminate warp mid-flight. These are the known safe ways across.
The driver does NOT implement waypoint-routing or accelerator-gating yet -- drive
these by hand or seed the survey with a wormhole placement past the well.

## Belt accelerators (Asteroid Belt Beta / Gamma / etc.)
- The belt's main body sits across a gravity well; direct warp will not cross it.
- Traversal = the **Accelerators** (Accelerator Sphere / Beltway navs). An
  accelerator **is a gate**: warp to it, then cross with the **gate action**
  (`enb.gate()` / `cross_gate`), not a raw click. It transits you across the well
  to the next belt segment.
- So belts ARE surveyable: treat each accelerator as a routable transit gate and
  chain across segments.

## Stuck in a well
- **HOLD Z** (keep it held -- not a single tap) to fly backwards until clear of the
  well, then warp back near where you came from and take a different route.

## Akeron's Gate (well in the middle of the sector)
- You CANNOT warp directly from Akeron's Gate to "Sector Gate to Saturn" or to the
  Aragoth system gate -- a well sits between.
- Warp to the **Ekobi Maru** nav first, THEN warp to the target gate.

## Freya (safe path only -- otherwise pirate-lethal + wells)
From **Akeron's Gate to Sol System** (the entry nav inside Freya):
1. Warp to **Folkvang**. Do NOT linger more than a few seconds -- combat-lvl-30
   mobs. Warp in, immediately warp out again.
2. From Folkvang, warp to EITHER:
   - **Sector Gate to Ragnarok** → then reach **Sector Gate to Nifleheim Cloud**.
   - **Sector Gate to Jotunheim** → then reach **System Gate to Alpha Centauri**
     or **System Gate to Proxima Centauri**.

Follow these exact paths or you hit a well. Distance-inefficient but safe and
repeatable -- Freya is NOT dangerous along this path (don't blanket never-explore
it for these transits).
