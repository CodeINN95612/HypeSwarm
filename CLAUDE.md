# Hype Swarm — working rules

3D co-op horde-survival ARPG. 1–5 players, ~60-minute sessions ending at a boss.
Unity 6 (`6000.6.0f1`), URP, Mirror netcode, IL2CPP.

**The design authority is [`Docs/GAME_SPEC.md`](Docs/GAME_SPEC.md).** Read the relevant section
before implementing anything; do not re-derive a decision it already made. This file holds only
what the spec omits (testing) and the invariants easiest to break by accident.

Drive the Editor through the Unity CLI / MCP bridge. Never hand-edit `.unity`, `.prefab`, or
`.asset` YAML — GUID/fileID corruption is silent and expensive.

**Save assets before committing.** Unity holds edited assets in memory and writes them on its own
schedule, so `git add` can capture an asset that still has the old values on disk — a commit that
looks right in the Editor and is wrong in the repo. Run `AssetDatabase.SaveAssets` (or Ctrl+S)
first, and check the staged diff of any `.asset` you touched.

---

## Build order

Phases 1–8 are a complete game without progression; if only part of this ships, that's the part
that must be good.

| Phase | Steps | Gate |
|---|---|---|
| 1 Foundation | asmdefs, input, content registry, tuning config, IL2CPP · controller & camera · Mirror lobby | builds clean, asmdef boundaries enforced |
| 2 Core combat | stats · damage pipeline · ability system · **horde** · wave director + XP | ★ 2000 enemies · 5 players · 60fps · then **Playtest 1: is it fun bare?** |
| 3 Systems | tags/keywords/hooks · **trace tooling** · augments · items & shop · paths · death & revive · coordination | **Playtest 2: do people talk?** |
| 4 Structure & meta | siege/Rest Stages · boss framework · persistence · content scale-up | ★ three unlike champions with no bespoke C# · fresh account finishes solo |
| 5 Ship | 5-player UX · perf pass · Steam · (optional) dedicated server · Early Access |

Risk gates exist to be failed *early*. If the horde gate fails, change architecture there — not
after two hundred augments sit on top of it.

## Invariants

These are the ones that rot quietly. Each has a test obligation below.

- **Stat operation order is fixed:** `(base + flatAdd) × (1 + Σ percentAdd) × Π (1 + percentMult)`.
- **Bounded stats are stored linear and derived asymptotic:** `effect = value / (value + K)`. Never cap a stat.
- **Abilities read stats; stats never carry behaviour or tags.** The dependency runs one way.
- **No runtime state in ScriptableObjects.** `AbilityDefinition` is immutable; state lives in `AbilityInstance`.
- **Hook priority ordering is mandatory** — flats resolve before multipliers, so pick order cannot change outcomes.
- **Augments are rules over tags, never over named champions.**
- **No effect step instantiates a particle system or plays a sound.** Steps emit events; presentation subscribes.
- **`Shared` never references `UnityEngine.UI`, particle systems, or `AudioSource`.** The asmdef boundary is the enforcement.
- **Content is addressed by stable string ID** (`augment.shield_amp`), never Unity GUIDs or object references across the wire.
- **Trash mobs never carry ability instances.** Elites and bosses use the full system; the boundary is explicit in code.

---

# Testing policy

The spec does not cover tests. This section does, and it is binding.

**Rule of thumb: test the things that have a right answer.** A formula, an invariant, an ordering,
a boundary, a lifecycle. Do not test the things that only have a *feel* — those are what the
playtest gates are for.

Tests ship in the same commit as the code they cover. A phase step is not done until its tests
are green.

## What must be tested

**1. Every formula and documented ordering.** Stat operation order, asymptotic derivation, scaling
coefficients, step-function thresholds, XP curves, price formulas. These are the cheapest tests in
the project and the most expensive bugs — a wrong operation order means rebalancing every item in
the game later.

**2. Every invariant listed above, and every entry in the spec's "Known traps" list that is
checkable in code.** That list is a test manifest; treat it as one. In particular:

- Two `AbilityInstance`s from one `AbilityDefinition` have independent cooldowns.
- Applying augments in a different order produces an identical result.
- A triggered effect that feeds itself is refused at the depth guard and logs loudly.
- Removing a modifier by source leaves the sheet exactly as it was before it applied.
- A derived bounded stat never reaches 1.0, at any input including absurd ones.
- An augment bag never repeats within a run, depletes correctly, and resets only on exhaustion.
- Every content ID is unique and resolves; no definition references a missing ID.
- `Shared` has no forbidden assembly reference (assert over the asmdef files themselves).

**3. Anything host-authoritative or seed-driven.** Same seed plus same inputs produces the same
spawn sequence — this is a correctness requirement of the netcode, not a nicety, because clients
simulate trash mobs locally from that seed. Also: loadout validation rejects illegal slot counts,
unknown rune IDs, and over-budget totals; the signed save round-trips and detects tampering.

**4. Lifecycles that leak.** Floor-entity despawn timers, pet caps, projectile pools, modifier
removal on unequip, augment removal. At horde density a leak is a crash, and leaks are invisible
until they aren't.

**5. Performance gates, automated.** The 2000-enemy gate is a test, not a manual check — add the
performance testing package at Phase 7 and assert a frame-time budget with the projectile and
summon load included. A gate you have to remember to run is a gate you stop running.

## What must not be tested

Say so out loud rather than writing a weak test:

- **Feel** — movement, camera, dash responsiveness, VFX timing. Playtest gates own these.
- **Balance values.** Test the *shape* of a tuning curve (monotonic, bounded, no divide-by-zero),
  never the constants. Tuning numbers change weekly; a test pinning one gets deleted or, worse,
  gets its expectation updated reflexively until it asserts nothing.
- **Unity itself.** No test that a `Transform` moves or a prefab instantiates.
- **Inspector wiring and trivial accessors.**
- **Whole-scene end-to-end flows** where an integration test at the seam would do. Slow, flaky,
  and they fail without telling you where.

## How

- **EditMode by default.** Pure C# logic — stats, bags, tag queries, hook ordering, registry,
  save — needs no scene and runs in milliseconds. Reach for PlayMode only when the game loop,
  physics, or networking is genuinely under test.
- **Test assemblies mirror the three-way split**: `HypeSwarm.Shared.Tests`, `.ClientOnly.Tests`,
  `.ServerOnly.Tests`. A test assembly may never widen a production assembly's references.
- **No test depends on wall-clock time or frame rate.** Inject the clock and the tick. This is
  also why passive auras tick on an injected schedule rather than in `Update` — testability and
  the §5.5.3 performance rule want the same design.
- **Seed every random path explicitly.** An unseeded test in this codebase is a flaky test.
- **Name tests for the behaviour, not the method**: `ModifierRemovedBySource_LeavesSheetUnchanged`.
- **Reuse the headless balance simulator** (Phase 19) as the harness for combinatorial property
  tests — champions × augments × items × party size is not explorable by hand.

## When a bug escapes

Write the failing test first, then fix it. A bug that reached a playtest is proof the policy above
has a hole; the regression test is how the hole gets closed.
