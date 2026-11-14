# AI task log

Chronological record of substantial work by ChatGPT, Claude or other contributors.
Newest entries go at the top. Link commits/PRs where available.

---

## 2026-11-15 — Establish shared AI collaboration notes

- **Contributor:** ChatGPT
- **Branch:** `feature/secondary-homing-delay-launch-angle`
- **Task:** Establish a durable shared project brief and handover system for Claude and ChatGPT.
- **Status:** Documentation authored and committed to this feature branch; Unity build/tests not applicable.
- **Work performed:**
  - Recorded the long-term goal: a faithful GOF2 remake expanded into a coherent persistent multiplayer universe.
  - Documented player-facing pillars, architecture constraints, multiplayer correctness requirements and workflow.
  - Established four shared files: project context, task log, changelog and current handover.
  - Added a pointer from `CLAUDE.md` so a Claude Code session can discover the collaboration notes.
- **Important boundary:** This documentation task does not implement the secondary weapon fix. The existing
  branch's source implementation remains as it was when this note was written.
- **Review requested:** Claude should read the context and handover, check the design against the actual code,
  and call out any inaccurate architecture assumptions or missing multiplayer implications.

## Earlier work before this log existed

- The user established that ChatGPT and Claude should work as separate contributors, with the user manually
  passing work between them. No automated assistant-to-assistant communication is expected.
- The user identified `SpectreAnims/Galaxy-on-Fire-2-Unity-Remake` as their fork. Do not use a different fork.
- Claude created the branch `feature/secondary-homing-delay-launch-angle`.
- A review found the branch's initial launch pitch/yaw were read from item attributes, despite the intended
  behaviour being hardpoint-specific. The launch angle tests also encoded the item-level approach.
- Existing ship/kit mount documentation describes a per-mount `rotation` (Euler or quaternion) that turns the
  weapon model and muzzle. It currently documents shots as continuing along the ship's nose. This existing
  rotation is a strong candidate for the source of launch orientation, but the implementation path must be
  inspected before deciding how to use it for projectile velocity.
- No source correction to that branch has been made by this documentation task.
