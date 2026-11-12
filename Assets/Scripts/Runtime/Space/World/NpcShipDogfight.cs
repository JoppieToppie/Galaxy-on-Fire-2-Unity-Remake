// NpcShipDogfight.cs
// NpcShip, part: Galaxy on Fire 3's way of fighting (remake option Settings.NpcDogfight, a session rule; Flight.Gof3Dogfight
// the rules): a fighter of a mod hull with "traffic": { "ai": "gof3" } (or every fighter) makes attack runs at its target
// and veers off instead of circling inside the break-off range, sits on its tail now and then at 1.6x speed, teases it by
// flying just ahead of its nose, leads its shots with an aim that settles, and barrel-rolls away from hits (invulnerable
// for 1.5 s of the 1.8 s roll). UpdateDogfight runs after the targeting each frame; Steer asks DogfightAim where to fly
// and whether to fire, and DogfightRoll for the roll's turn and side step; OnDamaged starts a roll. Not for wingmen,
// freighters, turrets, fixed objects or capital ships, nor while an event graph's order steers the ship. The roll turns
// the model only: the other players in a session see the ship's path, not its spin.
// Formations (GoF3's FormationSystem: V formations, followers within 90 units of their slot, spacing from the leader's
// size): while a local patrol isn't fighting, such a fighter takes a V slot behind its group's leader (the first living
// local fighter of its race: GoF2's locals start round one point and patrol their race's route), turning and matching
// speed to keep it (FormationSlot); a fight breaks it up.

using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class NpcShip
    {
        /// <summary>The roll's side step: the ship's speed x this, sideways, while it rolls.</summary>
        const float RollSideStep = 0.8f;
        /// <summary>GoF3's 90 units (x 11.4) between slots at least, else 3 x the leader's hit size.</summary>
        const float FormationSpacingUnits = 90f * Gof3Dogfight.DistanceScale;
        const float FormationLookupMs = 1000f;

        void SetupDogfight(SpawnSpec spec, Database db)
        {
            dogfight = null;
            dogTarget = null;
            if (spec.freighter || spec.fixedObject != null || spec.turretAssembly != null || spec.capital != 0 || spec.capitalPart || spec.modCapital) return;
            int mode = Settings.NpcDogfight;
            if (mode == SessionRules.DogfightOriginalMode) return;
            if (mode == SessionRules.DogfightModHulls
                && !string.Equals((db?.Ship(spec.ship) as CustomShipData)?.traffic?.ai, "gof3", System.StringComparison.OrdinalIgnoreCase)) return;
            bool boss = spec.wantedIndex >= 0 || spec.pirateEvent == SpawnSpec.EventBoss;
            dogfight = new Gof3Dogfight(Gof3Dogfight.EvadeChance(Session.Difficulty, boss), () => Random.value);
        }

        /// <summary>It fights GoF3's way this frame: attacking a target on its own (no route, order, wingman duty).</summary>
        bool Dogfighting => dogfight != null && !IsWingman && attacking && target != null && !followingWaypoint && order == null && mindControlMs <= 0f;

        /// <summary>After the targeting: the target's motion (for the lead), the mode, the roll's invulnerability.</summary>
        void UpdateDogfight(float dtMs)
        {
            if (dogfight == null) return;
            dogfight.Tick(dtMs);
            if (target != dogTarget)
            {
                dogTarget = target;
                dogfight.NewTarget();
                dogTargetVel = Vector3.zero;
                if (target != null) dogTargetLast = target.transform.position;
            }
            else if (target != null && dtMs > 0f)
            {
                var p = target.transform.position;
                dogTargetVel = Vector3.Lerp(dogTargetVel, (p - dogTargetLast) / dtMs, 0.2f);   // metres a ms, smoothed
                dogTargetLast = p;
            }
            if (Dogfighting)
            {
                var away = transform.position - target.transform.position;
                float r = NpcTables.BreakOffRange * M;
                dogfight.Step(dtMs, new Gof3Dogfight.View
                {
                    distance = away.magnitude / M,
                    insideBreakOff = Mathf.Abs(away.x) < r && Mathf.Abs(away.y) < r && Mathf.Abs(away.z) < r,
                    targetFacing = away.sqrMagnitude > 1e-6f ? Vector3.Dot(target.transform.forward, away.normalized) : 0f,
                });
            }
            bool shielded = dogfight.Invulnerable;
            if (shielded && !rollInvulnerable && !Target.invulnerable) { Target.invulnerable = true; rollInvulnerable = true; }
            else if (!shielded && rollInvulnerable) { Target.invulnerable = false; rollInvulnerable = false; }
        }

        /// <summary>Steer's point to fly at instead of the aim spot (Unity space) and whether to hold fire: away from the
        /// target veering off, across it holding back, ahead of its nose teasing, behind it chasing until lined up; at it,
        /// led by its motion with the settling miss, while approaching.</summary>
        Vector3 DogfightAim(Vector3 aimSpot, out bool holdFire)
        {
            var t = target.transform;
            var pos = transform.position;
            holdFire = true;
            switch (dogfight.Current)
            {
                case Gof3Dogfight.Mode.VeerOff:
                {
                    var away = pos - t.position;
                    if (away.sqrMagnitude < 1e-6f) away = transform.forward;
                    return pos + (away.normalized + t.forward * 0.25f).normalized * 1000f;
                }
                case Gof3Dogfight.Mode.Idle:
                {
                    var side = Vector3.Cross(t.position - pos, Vector3.up);
                    if (side.sqrMagnitude < 1e-6f) side = transform.right;
                    return pos + side.normalized * 1000f;
                }
                case Gof3Dogfight.Mode.Tease:
                {
                    return t.position + t.forward * (Gof3Dogfight.TeaseAhead * M) + t.right * (dogfight.teaseSide * Gof3Dogfight.TeaseSide * M);
                }
                case Gof3Dogfight.Mode.Chase:
                {
                    var slot = t.position - t.forward * (Gof3Dogfight.ChaseBehind * M);
                    if ((slot - pos).sqrMagnitude > 2000f * M * 2000f * M) return slot;
                    break;   // on its tail: fire, as approaching
                }
            }
            holdFire = false;
            // AIJob aiming (ECS::aimingPoint): the target's place when the shot gets there, and a miss settling to 0
            float shotSpeed = Mathf.Max(0.1f, gunSpeed) * M;   // metres a ms
            float flightMs = Vector3.Distance(pos, aimSpot) / shotSpeed;
            var led = aimSpot + dogTargetVel * flightMs;
            float miss = dogfight.AimError;
            if (miss > 0f) led += Random.insideUnitSphere * (miss * 1000f * M);   // within a GoF2 hit cube (+-1000 units)
            return led;
        }

        /// <summary>A local patrol flying its route in a V behind its leader: the slot (Unity space) and a speed factor that
        /// closes on it (faster behind it, slower ahead); false = it flies on its own (the leader, fighting, alone).</summary>
        bool FormationSlot(float dtMs, out Vector3 slot, out float speedFactor)
        {
            slot = default;
            speedFactor = 1f;
            if (dogfight == null || traffic == null || Spec.group != NpcGroup.Local || attacking || !followingWaypoint || order != null || IsWingman) return false;
            if ((formationLookupMs -= dtMs) <= 0f)
            {
                formationLookupMs = FormationLookupMs;
                formationLeader = null;
                formationIndex = -1;
                int k = 0;
                foreach (var s in traffic.Ships)
                {
                    if (s == null || s.Spec.group != NpcGroup.Local || s.Spec.race != Spec.race || s.IsWingman || s.Gone
                        || s.Current != State.Fly || s.Target == null || !s.Target.Alive) continue;
                    if (formationLeader == null) { formationLeader = s; if (s == this) break; continue; }
                    if (s == this) { formationIndex = k; break; }
                    k++;
                }
                if (formationLeader == this) formationLeader = null;
            }
            var lead = formationLeader;
            if (lead == null || formationIndex < 0 || !lead.Target.Alive || lead.attacking) return false;
            float spacing = Mathf.Max(FormationSpacingUnits * M, lead.Target.radius * 3f);
            var (row, side) = Gof3Dogfight.VSlot(formationIndex);
            var lt = lead.transform;
            slot = lt.position - lt.forward * (row * spacing) + lt.right * (side * row * spacing);
            // ahead of the slot (along the leader's heading): ease off; behind it: catch up
            float behind = Vector3.Dot(slot - transform.position, lt.forward);
            speedFactor = Gof3Dogfight.FormationSpeed(behind / spacing);
            // aim past the slot along the leader's heading, so it flies alongside instead of circling the point
            slot += lt.forward * spacing;
            return true;
        }

        /// <summary>The roll under way: the model's turn about its nose (degrees) and the side step this frame (Unity metres).</summary>
        float DogfightRoll(float dtMs, out Vector3 step)
        {
            step = Vector3.zero;
            if (dogfight == null || !dogfight.Rolling) return 0f;
            float p = dogfight.RollProgress;
            float eased = p * p * (3f - 2f * p);
            step = transform.right * (dogfight.RollSide * speed * RollSideStep * dtMs * M * Mathf.Sin(p * Mathf.PI) * 1.57f);
            return -dogfight.RollSide * 360f * eased;
        }
    }
}
