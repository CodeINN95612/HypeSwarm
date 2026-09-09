# Technical Design Specification
## Co-op Horde Survival ARPG — Working Spec

**Status:** Pre-production
**Engine:** Unity 6 LTS (URP)
**Scope:** 1–5 player co-op PVE, ~60 minute sessions
**Excludes:** Art direction, models, audio, music, champion fantasy, enemy fantasy. This document covers systems, architecture, and build order only.

---

# Part I — Game Context

## 1. The pitch

A 3D co-op horde survival game. One to five players pick champions with distinct kits and fight escalating waves of enemies in a single continuous session lasting roughly an hour, ending at a boss. Between the fighting, players make build decisions — augments, items, stat upgrades — that compound into run-defining power.

The reference points are Vampire Survivors for the moment-to-moment (dense hordes, rapid power growth) and League of Legends Arena for the decision layer (augments, prismatic items, ability modifiers, synergies). The differentiator is that it is a **team** game where coordination is mechanically rewarded rather than merely convenient.

## 2. Design pillars

These are the load-bearing commitments. When a design decision is ambiguous, resolve it toward these.

**Pillar 1 — Coordination is mechanically rewarded, not just socially encouraged.**
Combos between players, shared drafting, item trading, cross-player augments. If a system could be designed so that five people playing solo in the same room performs as well as five people talking, it has been designed wrong.

**Pillar 2 — Decisions compound.**
Power comes from interactions between choices, not from raw stat accumulation. A player should be able to point at three specific picks and explain why their build works.

**Pillar 3 — Commitment is rewarded, not punished.**
Going deep on a build path must be viable despite randomness. The systems bend toward the player who commits.

**Pillar 4 — Nobody gets overruled into nothing.**
Team decisions are additive. A player who loses a vote still receives something. Frustration in a 60-minute co-op session compounds badly.

**Pillar 5 — The session has an arc.**
Defined beginning, escalation, boss, end. Endless exists as an opt-in mode, not the default. Five people cannot commit to an open-ended session.

## 2.5 Value hierarchy

When two systems compete for importance, resolve in this order. This is the tiebreaker for balance disputes, reward sizing, and UI prominence.

| Rank | Asset | Character |
|---|---|---|
| 1 | **Teamwork** | The most valuable thing in the game. Nothing should out-earn coordinating. |
| 2 | **Structure defense** | Only three per run. Losing one must be a deliberate sacrifice, never incidental. |
| 3 | **Augments** | ~6 per run. Run-defining. A run without them cannot be won. |
| 4 | **Gold** | Moderate effort to earn, recoverable by selling items, transferable between players. |
| 5 | **Stats** | Accrued automatically, cheaply purchasable. Close to irrelevant individually. |
| 6 | **Meta runes** | A small head start; changeable between runs. |

Two notes on the extremes.

**Stats being near-worthless individually is correct and intentional** — they are the substrate abilities scale from (§5.6.5), not a reward. Their value is aggregate and passive. If a single stat purchase ever feels exciting, something is mispriced.

**Meta runes rank last for power, which is right, but they carry more weight than the ranking suggests in two ways.** They shape the *early* run, which is where a build's direction is set, and they are the only thing that persists between sessions — so they carry retention load out of proportion to their power budget. Rank them last for balance purposes; do not treat them as unimportant for player experience.

## 3. Session structure

```
START
  ↓
[ Combat — waves escalate continuously ]
  ↓  ... interrupted by:
  ├── Level-up ......... every ~30-60s   (stat pick, minimal interruption)
  ├── Augment milestone  ~6 per run       (heavy slow-mo, ~15s real)
  ├── Shop .............. periodic         (majority-triggered full pause)
  ├── Siege event ....... ~3 per run       (3 towers, save one → Rest Stage)
  └── Final boss ........ end of run
  ↓
END (~60 min) → final boss defeated → achievements banked
                                   → optional endless mode (separate, no boss)
```

### 3.1 The power curve inverts

**Density scaling changes what "good play" means over the course of a run, and this is a feature to design around rather than an accident.**

Early, the swarm is sparse and **evasion dominates**. Mobility, dashes, and positioning are the strongest tools; a skilled player simply does not get hit.

Late, the swarm is dense enough that dodging becomes impossible — there is nowhere to dodge *to*. Evasion stops scaling, and if the team has built for it, **mitigation and area damage take over**: the support's protection, the tank's durability, the explosive AoE that clears space. Defense becomes the best offense, because surviving long enough to keep dealing damage is the whole problem.

Three consequences:

- **Role value rotates through the run**, so every composition has a window where it shines. This delivers the "roles matter without being mandatory" goal for free, as an emergent property of density scaling rather than an authored rule.
- **Mobility-heavy champions need a late-game answer.** If a champion's entire kit is evasion, they become a passenger by minute 45. Every champion needs at least one line of scaling into mitigation, area damage, or team utility — check this explicitly when authoring each one.
- **It justifies the team systems.** Protection, shields, and revives are not "nice to have"; they are the mechanics the endgame is actually built on.

**Difficulty scaling rule:** scale enemy *density, composition, and elite modifiers* aggressively. Scale raw enemy *stats* gently. Exponential stat scaling produces a sudden wall where nothing dies and everything one-shots. Player power compounds naturally through augment interaction — that is where the power fantasy lives.

## 4. Progression layers

Four distinct layers with different cadence, weight, and social character. Keeping them distinct is what prevents the game from feeling like one undifferentiated stream of upgrades.

| Layer | Cadence | Weight | Who decides | Interrupts play |
|---|---|---|---|---|
| Level-up (stats) | ~30-60s | Small | Individual | Minimal |
| Augment milestone | ~6 per run | High | Individual, visible | Full pause, 15–20s |
| Items + stats | On drop / shop | Medium | Individual, tradeable | Full pause, timed |
| Rest Stage (siege) | 0–3 per run | Run-defining | Team, by commitment | Full stop |
| Account progression | Between runs | Meta, bounded | Individual loadout | N/A |

### 4.1 Level-ups (automatic stat growth)

XP is a **single team-wide pool**. Everyone contributes to it, everyone levels at the same moment, and the whole party is always the same level. Nobody falls behind, and difficulty tuning has one level curve to target instead of five.

**Level-ups are not a choice.** Each champion has an authored growth profile and gains its own stats automatically — one champion accrues damage reduction, another damage, another haste. This is the League model, and it is what makes level-ups an expression of champion identity rather than a menu.

**There is no ability levelling.** Abilities do not rank up. They get stronger only because the stats they scale from got bigger (§5.6.5).

The only time a level-up presents a decision is when the player has bought **stat selection** at the shop (§4.3) — an optional purchase that converts automatic growth into a choice for some period. Absent that purchase, levelling is silent and instant, with no interruption at all.

### 4.2 Augment milestones

Augments are **rare and weighty** — roughly **6 per player per run**, depending on milestones reached before the boss. They are the second most valuable thing in the game (§2.5), and a run without them cannot be won.

**Full global pause, 15–20 seconds.** Slow-motion was the wrong call. Late in a run a player can be fully surrounded, and even at 5% timescale the pressure to pick fast is enormous — which defeats the purpose of a decision this important. At six occurrences per run, a hard pause costs about two minutes total. That is affordable; a rushed augment choice is not.

Live visibility of teammates' hovered choices, real conversation, no timer pressure beyond the window itself.

**Trigger on mixed milestones, not level count alone.** A blend of level thresholds, elite kills, and elapsed-time markers keeps pacing predictable enough to plan around but not perfectly schedulable.

#### 4.2.1 The bag: draft without replacement

Augments are drawn from a **bag**, not rolled independently each time:

1. Each selection draws **3 augments** from the bag.
2. Each of the three has **its own single reroll**. Rerolling replaces that slot with a new draw; it cannot return the augment it just replaced.
3. **Everything seen leaves the bag permanently** — offered, rerolled away, or taken.
4. Later selections draw only from what remains.
5. The bag resets only if it runs dry, which should not happen for augments.

The player never sees the same augment twice in a run. That is what makes the reroll a real gamble, and the interesting case is the escalation:

> *"Slot 1 is good, but Augment 7 is what my build actually wants — if I reroll slots 2 and 3, maybe I hit it."*
> *"...no luck. Do I take the safe pick, or burn my last reroll on slot 1 and commit to the chase?"*

That second decision is the one worth designing for. Rerolling the good option destroys it permanently, so the player is choosing between a known solid pick and a shrinking bag that may no longer contain what they are hunting. Independent per-slot rerolls are what create it — a single reroll-all would collapse the whole thing into one coin flip.

> **Bags must be per-player, not shared.** With 6 selections × 3 offers × up to 3 rerolls, a single player can burn up to **36 augments per run**. Five players sharing one bag would consume 180, which forces an enormous pool and makes late offerings thin and random. Per-player bags also let two players pursue the same build without competing for the same cards.
>
> **Content target: 60–80 augments minimum** for the pool to comfortably outlast one player's 36 draws. This is a hard requirement, not an aspiration — below it, late-run offerings degrade and the "never see it twice" promise breaks.

### 4.3 Items and the shop

Individual wallets. Items drop from elites and are purchasable at the shop.

**Items use the same bag behaviour as augments** (§4.2.1): a shop visit offers 3 items, each individually rerollable once, and everything seen leaves the bag. The item pool is smaller than the augment pool, so **bag resets are expected and acceptable here** — seeing an item twice in a long run is fine.

**Items come in three shapes:** *weapons* that fire on their own timer, *enablers* that change how your casting behaves, and pure *stat blocks*. Weapons and enablers are where keyword synergies live (§6.2).

**Slots are capped at roughly six.** This is what makes trading meaningful: handing the tank's item to the carry costs a slot, so it is a real decision rather than a free transfer. It is also a performance ceiling (§11).

**Selling, at a considerable loss.** Items sell back for well under purchase price. The loss is what stops selling from being a free reshuffle — swapping your build every shop should hurt, so that early commitment stays meaningful (Pillar 3). It still guarantees a slot is recoverable and a dead item becomes *some* contribution: sell it, gift the gold to the teammate whose build wants what is on offer.

**Trading and gifting happen only at shops and Rest Stages.** No in-run trading UX. Handing items around mid-fight would be a menu in a bullet-hell, and the pause is what makes the conversation possible anyway.

**Full inventory means elite drops are not picked up.** With a six-slot cap, a full inventory leaves the drop on the ground rather than forcing a discard prompt mid-combat. The player sells or reorganises at the next shop; the interesting decision belongs there, not during a fight.

**Items and floor entities despawn on a timer.** Ground loot, corpses, and spawned props all expire. At horde density this is a performance requirement, not a design choice (§11) — and it gives the "full inventory" rule teeth, since an unclaimed drop is genuinely lost.

**Stat selection is a purchase.** Buying it converts level-up growth into a player choice (§4.1). Bare stats are also directly purchasable, priced below item efficiency (§5.6.4), so gold is never dead and a bad shop roll is never a wasted visit.

#### 4.3.1 Shop trigger and pause

A shop marker spawns in the world. When a **majority of living players** are inside its radius, the shop opens and the game **fully pauses for everyone**. It closes after a fixed window, or immediately once all players ready up.

- **Majority triggers it, but the pause is global and all living players can shop** regardless of where they were standing. Nobody is locked out for being on the far side of a fight they were handling.
- **The timer is the anti-frustration mechanism.** No single player can hold four others indefinitely. Ready-up only ends it *early*.
- **Majority, not unanimity**, so one AFK player cannot block the shop.
- **Dead players do not interact with the shop.** They cannot buy, sell, or trade. This makes reviving before a shop window urgent.

**Dead players keep their gold, and bank the missed visit.** A player who misses a shop while dead loses nothing permanently: their gold is untouched, and if they are alive at the next shop, they get a **double allocation** — twice the offering, or two purchases from the bag.

This is the right fix for the compounding problem. Missing a shop while dead is already a real cost in tempo, since the team fights a whole cycle with one player under-equipped. Banking the visit means death costs you *timing* rather than an entire economic cycle, which keeps the incentive to revive without turning one bad death into a run-ending spiral.

**Item trading between players is a core feature, not a convenience.** The team's item pool is a shared resource the group allocates. The full pause is what makes it practical — five people can actually talk here, which is impossible while dodging.

### 4.4 Rest Stages (Siege events)

Rare (0–3 per run) and **earned, not scheduled**. Roughly **three** siege events per run.

**The event.** At set points the game escalates hard and spawns a dedicated siege swarm. Simultaneously, **three structures begin constructing** at separate locations on the map, each displaying a different reward. The team can save **only one**. Whichever completes first fires — clearing every enemy on the map — and the other two immediately collapse.

Enemies attack all three. Undefended structures take steady, visible damage. The team must physically commit to one and abandon the others.

**The decision is expressed through movement, not a menu.** This is the strongest property of the design. Choosing is walking somewhere and holding it, splitting up is punished by the swarm rather than by a rule, and the whole team's commitment is legible at a glance. It converts a UI vote into a co-op moment.

**How exclusivity is enforced — it falls out of the rules rather than needing one.** All three structures build progress on the same timer and take damage from the swarm; undefended ones decay visibly. The event ends the moment **any one structure completes**, and the other two collapse.

Only one can be first, so saving multiple is impossible by construction. No artificial rule, nothing to explain, and it cannot be defeated by an overpowered late-run team. "Last one standing" is the same outcome from the player's side — you protect one, the others die — but framing it as a completion race is what makes it airtight, because it also handles the case where a very strong team keeps two alive.

**Structures are durable enough to change your mind.** This is the mitigation for the loss of pre-planning, and it must be tuned generously. A team should be able to start defending one, read the fight, and rotate to another without that being an automatic loss.

**Restore planning with a pre-swarm window.** Spawn the structures and their reward previews slightly *before* the swarm arrives. A short window to read all three rewards and call a target restores the strategic layer without removing in-the-moment pressure.

> **Design for the dither.** A team that hesitates between two structures and loses both is the signature failure of this mechanic, and it is a *good* one — a real coordination lesson that produces a story. Make it legible rather than arbitrary: clear progress and health bars on all three, visible decay when undefended, unmistakable feedback about which one the team is actually holding.

**Failure is cheap by design.** If all three fall, the run continues with no Rest Stage. Enemies already spawned do not despawn, but the spawn *rate* drops back down. This is the team correctly concluding they were not strong enough yet, or that nothing on offer was worth it. No death spiral.

> Failure cannot be *free*, or attempting is always correct. **The swarm is the cost** — the escalated wave stays on the map and must be survived or kited down, and deaths during it burn respawn charges (§5).

**Rewards are previewed, and should be pacts rather than upgrades.** Bidirectional trades with real costs, so that the choice between three is genuinely contested and walking away is reasonable:

> *"Choose two augments at each milestone, but lose 5% max HP per augment taken."*

Excellent late-game for a tanky composition, close to unplayable for a squishy one. If all three structures offer straight upgrades, the choice collapses into "whichever is closest" and the mechanic asks no question.

**Resolution (Pillar 4).** The saved structure's reward applies to the **whole team at full power**.

Pillar 4 was previously carried by the vote's scoped-additive model, which is gone. Replace it here: at the Rest Stage, each player individually picks **one reward from the two lost structures, at reduced power**. The structures you failed to save still echo.

This keeps nobody-gets-nothing intact, gives the abandoned options a purpose, and softens the sting of losing an offer your build wanted — without undermining the team-level commitment, since only the saved structure is global and full-strength.

**The Rest Stage itself has no timer.** Players signal ready; it ends when all are ready.

### 4.5 Account progression (meta)

Completing achievements earns points, spent on a **loadout of persistent bonuses** applied at run start. LoL runes are the reference. Cosmetics unlock from the same currency.

**No in-game payments of any kind.** Achievements are the sole source.

This is the **only** persistent power system in the game. Every run targets the same final boss; there is no ladder. Endless is a separate mode with no boss and unbounded escalation.

### 4.5.1 Where progression data lives

Steam achievements cannot be the source of truth: they are client-reportable, do not exist on a dedicated server or non-Steam build, cannot be revoked once granted, and tie progression to one platform permanently. Track conditions in your own data and *mirror* completions to Steam for display only.

**On local file versus database — the exploit question is smaller than it looks.** This is PVE co-op with tiny bounded bonuses, no leaderboards, no trading, no ranked ladder. A player who edits their save is cheating only themselves, in their own lobby, for a marginal advantage.

**Recommendation: local file, signed, plus host-side validation.**

- **Local save with an HMAC signature.** Stops casual text-editing. A determined user can extract the key from an IL2CPP binary, and that is acceptable — the cost of stopping them is wildly out of proportion to the harm.
- **Host validates joining players' loadouts.** This is the protection that actually matters and it is free. On join, the host checks the rune loadout against legal bounds: slot count, known rune IDs, budget total. A tampered save cannot bring an illegal loadout into someone else's game, which is the only exploit with a victim.

A backend (PlayFab, Supabase, Firebase free tiers) is only worth it if leaderboards, cross-device progression, or anything competitive appears later. Build the save layer behind an interface so that swap stays cheap, and do not pay for infrastructure to defend against self-cheating.

### 4.5.2 Keep it small and tune around party size

The bonuses are deliberately marginal:

> *A stat selection at run start at 200%* — a 100 max HP stat becomes 200 HP.
> *Currency every N enemies* — alongside an existing gold-per-kill or gold-per-minute stat, this is a nudge, not an engine.

The point is not power. It is **enabling a slightly earlier start on a synergy path** — a little more gold to try a second item, a stat seed that suggests a direction (§4.5.3). A rune changes what you *try*, not whether you win.

**Because the bonuses are marginal, tune difficulty on party size alone.** One curve per player count. Runes are noise against that and should not enter the tuning model at all.

Rules that still hold:

- **Never touch tags, keywords, or the augment pipeline.** Generic stats, economy, and run-start variance only. Otherwise part of the build is decided in a menu before the run starts.
- **Loadouts are freely changeable between runs**, never within one.

### 4.5.3 Loadout, not accumulation

Players select a **limited number of slots** from a growing pool rather than accumulating everything. Unlocking more options widens choice; it does not raise the ceiling. Total power is fixed by slot count regardless of how many options exist.

**The random-stat rune wants to become a build seed, and that is its real value.** A run that opens with 200% haste gives the player a reason to steer toward cooldown-driven items and augments from the first minute, feeding pool weighting and path thresholds (§6). That is worth more than the raw stat, and it is why the variance tier is more interesting than the linear one even though both are small.

### 4.5.4 Achievements should reward variety, not grind

Design conditions around **breadth** — play each champion, win with each role composition, complete a run committed to a single path, defeat the boss without losing a respawn charge, land a combo chain of length N.

Avoid raw-quantity conditions ("kill 100,000 enemies"). Grind achievements convert the game into a farming loop and reward the least interesting way to play it.

Achievements requiring coordination (§7) are the best ones — they push Pillar 1 into the meta layer.

> **Cosmetics are out of scope for now.** They are easy to add once people are actually playing, and they cost content time that the augment pool (§4.2.1) needs far more urgently.

## 5. Health, death, and revival

Individual HP per player. No shared pool — it makes one player's mistakes drain everyone, invites blame, and deletes individual dodging skill.

**Team respawn charges.** Death removes you for a scaling timer, then respawns you at the team, consuming one charge from a team-wide pool. Run ends when the pool is empty and everyone is down. Charges are a *currency* — at Rest Stages and the shop, restoring charges competes with taking power. That is a genuine team decision.

**Multiple revive routes with different shapes.** The routes must differ in more than price; the situation should determine the answer.

| Route | Cost | Requirement | Purpose |
|---|---|---|---|
| **Map anchor** | Free, but single-use and finite | Living players hold proximity for several seconds | The scarce, positional reset |
| **Purchased anchor** | Very expensive gold **plus an inventory slot** while carried | Deploy to ground, then same proximity hold | Portable insurance, placed where you choose |
| **Channel revive** | Free, slowest | Living players channelling | Reward for surviving long enough |
| **Self-revive charge** | Expensive, pre-purchased | None | Insurance bought instead of damage |
| **Last stand** | Free | Final living player only | The clutch fantasy |

**Anchors are a map resource, not a purchase.** A finite number are scattered at random locations at run start. Holding proximity for several seconds **revives every dead player at once**, then the anchor deactivates permanently. Buying one is possible but priced as a genuine emergency measure.

This is a much stronger design than a purely purchasable anchor, and worth being deliberate about why:

- **The map becomes a strategic resource map.** Teams learn where the unused anchors are and fight near them when things are going badly. Retreating *toward* something is far more interesting than retreating away from something.
- **Scarcity is spatial, not economic.** A finite, non-renewable, location-bound resource creates pressure that gold cannot. You cannot save up for more anchors.
- **Reviving everyone at once makes it a comeback button**, which is what justifies the scarcity. A near-wipe recovered by a desperate run to a known anchor is the kind of moment people retell.

**Purchased anchors are carried items.** Bought at the shop, they occupy one of the six inventory slots (§4.3) until deployed. Once placed on the ground they leave the inventory and behave identically to a map anchor — single-use, proximity-hold, revives everyone.

The slot cost is what balances them, and it is a better lever than gold alone. Carrying insurance means giving up an item for as long as you hold it, so the anchor competes directly with build power rather than just with the wallet. It also means a full inventory blocks elite drops (§4.3) while you carry one, which is a quiet ongoing tax on playing safe.

**Deploying early versus carrying is the interesting decision.** Place it in advance and you have a known fallback and a free slot, but you have committed to defending that area. Carry it and you stay flexible — at the risk that a full team wipe leaves the anchor sitting unused in a dead player's bag, since deployment requires someone alive to do it. That tension is worth preserving; do not add an auto-drop on death.

Because they are items, anchors can also be handed off at shops and Rest Stages — the team pools gold, one player buys, another carries. That is a coordination beat for free.

Two things to watch: an anchor consumed early for one careless death is a real loss the team will feel later, which is correct but needs to be *legible* — mark used and unused anchors clearly on the map, deployed ones included. And map anchor placement must be seeded so distribution is reasonable, not clumped into one corner.

**Channel revive is the slowest route and rewards attrition.** It exists for the specific case where the surviving players kited and stalled long enough to get breathing room. That survival was expensive — time spent escaping is time not spent killing, earning gold, or defending structures — so the revive being free is already paid for. Do not speed it up; the slowness is what makes the preceding escape meaningful.

## 5.5 Combat model

### 5.5.1 Movement and controls

**Supervive-style, not League-style.** Direct WASD movement with aimed abilities — not click-to-move, not attack-move. The player is moving continuously and aiming independently.

This is the correct pairing for the rest of the design: constant motion is what a dense horde game demands, and it is why removing manual basic attacks makes sense (below).

**Netcode consequence.** With every ability aimed rather than targeted, hit registration matters far more than it would with click-targeting. Keep client-authoritative movement (§10), but let the client show its own ability effects and predicted hits **immediately**, with the host confirming damage. The presentation/logic split (§12) is what makes this possible, which is another reason it must hold from the start.

### 5.5.2 No manual basic attack

Stopping to auto-attack fights constant movement, so the manual basic attack is removed. Kiting still exists — it is done with abilities and movement rather than attack spacing (§5.5.4).

**Continuous damage comes from three places:**

- **Passives**, which are always-on fields or reactive rules (§5.5.3). This is the baseline every champion has from the first second of the run.
- **Abilities**, on cooldown, which is the bulk of it.
- **Items**, some of which carry their own firing behaviour on their own timer.

**Item shapes.** *Weapons* fire on their own timer. *Enablers* change how your casting behaves. *Stat blocks* do neither.

> *Bouncy Ball (weapon) — periodically spawns a ball that damages on contact and bounces off walls, gaining damage per bounce. Lasts 15s. +100 Damage, +300 Max HP, +10 Haste.*
> *Storm Cast (enabler) — every 15 seconds, energize your next ability. Energized abilities trigger Automatic Casts.*

These two illustrate a keyword in action — "Automatic Cast" is a named mechanic that both reference, which is what lets an augment later say *"Automatic Cast cooldowns are reduced by haste at 80% effectiveness"* and have it apply to both. Keywords are covered properly in §6.2; Automatic Cast is one example among several, not a foundation of the design. **The pillar is that synergies are expressible, not any particular synergy.**

**Consequences to design around:**

**Cap item slots** at roughly six. Five players × unlimited firing items is a performance problem that multiplies by party size (§11), a cap forces real decisions (Pillar 2), and it makes trading cost something (§4.3).

**Weapon behaviour is authored, not scaled.** Bounce counts, durations, travel patterns are fixed per item. Stats change how hard a bouncy ball hits, never how it bounces (§5.6.5). Augments are the only thing that may alter behaviour.

**Level geometry is a balance dependency.** The bouncy ball is worthless in an open field and excellent in a corridor. Once items interact with walls, arena layout becomes a balance surface — arenas need consistent obstacle density, or item value swings between stages. This serves the horde game anyway by creating chokepoints and cover.

**Networking:** item-spawned entities are host-authoritative for spawn and damage, with clients simulating visuals locally (§10). Do not replicate individual projectile transforms.

### 5.5.3 Fixed ability layout

Every champion has exactly five, and the shape is guaranteed rather than variable:

| Slot | Role |
|---|---|
| Passive | Always-on identity |
| Primary | Main damage / bread-and-butter |
| Secondary | Utility, control, or situational damage |
| Mobility | Dash, blink, or jump — **always present, always does something beyond moving** |
| Ultimate | Long cooldown, high impact |

**Guaranteed mobility on every champion is load-bearing**, not a convenience. With no basic attacks, all spacing comes from movement and dashes; a champion without mobility would have no way to create distance.

**Mobility abilities are a design surface, not a uniform dash.** Two shapes worth authoring:

> *Dash forward, striking with the sword for heavy area damage.* — mobility fused with offense; repositioning and damage are the same button, so escaping costs your damage window.
> *Three dash charges, each on its own cooldown, with a 0.2s lockout between uses.* — mobility as a resource to budget rather than a single escape.

**Mobility's value declines over a run** (§3.1). Early it is the dominant survival tool; late, with the swarm dense enough that there is nowhere to dash *to*, it stops scaling. A mobility ability that only repositions will feel dead by the endgame — which is exactly why every mobility slot should also *do* something, as in the first example above. The damage, the zone left behind, or the debuff applied is what keeps the slot relevant once the escape function saturates.

The charge model needs that inter-use lockout specifically to prevent accidental double-dashes. In a game where dashing is the primary survival tool, burning two charges on one input is the kind of feel bug that reads as unresponsiveness.

**The passive slot is a continuous behaviour, not an attack.** It is always running, requires no input, and expresses the champion's identity through a persistent field or reactive rule:

> *Claws — damages all enemies within a radius continuously; heals for 5% of damage dealt.*
> *Slow Field — enemies near the champion are slowed, more strongly the closer they are; allies inside are healed per second.*

Two things follow. Passives give every champion a **damage or utility floor** independent of items and cooldowns, which softens the early-run gap when the player has one item and long downtime. And a passive is where a champion's role is most legibly expressed — the two examples above are a self-sustaining bruiser and a support without either needing a role label.

> **Performance note:** five continuous radius queries against a 2000-enemy horde, every frame, is a real cost. Run passive auras on a fixed tick (a few times per second) through the spatial hash (§11), never per-frame, and never with physics overlap calls.

**Reconciling with indexed slots (§8.3).** The architecture keeps abilities in an indexed array — that flexibility is what makes "your third ability is replaced" a one-line change. The fixed layout is a *design convention* layered on top, expressed by giving each slot a **role tag** (`primary`, `secondary`, `mobility`, `ultimate`, `passive`).

Augments then target roles generically: *"your mobility ability leaves a damaging trail"* works on every champion in the game, present and future, without naming one. This is the tag philosophy (§6) applied to slots, and it is why the layout should be a tag rather than a hardcoded field.

### 5.5.4 Cast cost: how kiting works

Abilities are not uniform in what they cost to use. Each ability authors a **movement cost** alongside its cast time:

| Cost | Behaviour | Typical use |
|---|---|---|
| **Free** | No interruption; cast while moving at full speed | Sustain, shields, utility, most support kits |
| **Slowed** | Movement continues at reduced speed | Sustained channels, beams |
| **Rooted** | Movement stops for the cast duration | Burst damage, hard control |
| **Channelled** | Movement stops, effect builds while held, released or interrupted by the player | Scaling payoff with a commitment cost |

> *A laser with a 0.1s cast that roots.* Versus *a healing forcefield that does not.*

**This axis is what makes kiting exist without basic attacks.** Kiting becomes the decision of *when to pay the root cost* — you create space with movement and mobility, then spend a moment standing still to land the burst. That is a skill expression, and it is entirely absent if every ability is free to cast.

It is also the cleanest mechanism for **role differentiation under constant motion** (§6). A support whose kit is mostly free-cast can contribute while permanently repositioning; a burst champion pays for damage with vulnerability windows. Neither needs a role label — the cast cost profile *is* the role.

**Channelling is the richest of the four** because the player chooses how much to commit, in the moment, under pressure:

> *Channel up to 3 seconds, gaining +50 damage reduction throughout. On release, stun and damage enemies in a radius, both scaling with channel duration (up to 5s of stun). Heal for a percentage of damage taken while channelling. 8s cooldown.*

That single ability is a defensive cooldown, a teamfight setup tool, and a sustain source, and which one it is depends entirely on when the player lets go. It also inverts the usual risk: standing still is normally lethal, but this makes incoming damage into healing, so the horde closing in is what makes the channel *better*. Author more like this — abilities where the cast cost is the mechanic rather than a tax on it.

Two rules worth holding:

- **Rooted casts must be short.** In a dense horde game, a long root is a death sentence rather than a decision. Keep them in the fractions-of-a-second range and telegraph them clearly.
- **Mobility abilities are always free-cast**, or the slot cannot serve its escape function.

Cast cost is an authored property, tagged, and therefore augment-modifiable — *"your rooted abilities may be cast while moving"* is a strong, generic, run-defining augment that works across the whole roster.

### 5.5.5 Ranges are authored, not scaled

Ability ranges, radii, and shapes are **fixed per ability** and modifiable only by augments. There is no area or range stat.

This removes the most dangerous balance stat in the design — area scales quadratically in effect, so a linear area stat quietly multiplies damage — and it makes every ability's footprint a known, authored quantity that can be tuned and telegraphed reliably.

The same applies to item weapons: **behaviour is authored per item** — bounce counts, durations, travel patterns. Stats change how hard a bouncy ball hits, never how it bounces. Augments are the only thing that may alter behaviour.

> Projectile count and item cadence are now the multipliers to watch. Both read as linear but compound against dense clusters where projectiles overlap, and both scale with party size. Treat them with the suspicion area would have received.

## 5.6 Stat model

### 5.6.1 Unified damage, unified reduction

**One damage stat.** No AD/AP split. **One damage reduction stat.** No armor/magic resist split. Penetration is likewise one thing.

This removes an entire axis of itemization complexity, makes every damage item legible to every champion, and eliminates the dead-item problem where a drop is worthless because it is the wrong damage type.

> **Knock-on: the tag system's Scaling axis loses its main entries (§6).** AP and AD were carrying most of the build-direction differentiation. That work now moves to the **Source axis** — ability, projectile, damage-over-time, summon, zone, proc. This is the better outcome: "summon build" versus "projectile build" versus "DoT build" is more interesting and more visually distinct than "AP build versus AD build," which was only ever a number swap. Design paths around *how* you deal damage, not *what type* it is.
>
> Combined with the removal of basic attacks (§5.5.2), the Source axis is now doing nearly all the differentiation work in the game. Author it carefully — it is the backbone of build variety.

### 5.6.2 Store linear, derive asymptotic

Some stats break the game at 100%: damage reduction, cooldown reduction, dodge, lifesteal. Others are fine unbounded: damage, max HP, regen.

The wrong fix is to cap the stat. The right fix is to **keep the player-facing stat linear and unbounded, and derive the bounded effect from it.**

```
effect = value / (value + K)
```

This is the League armor formula and the reasoning behind ability haste. It never reaches 1, it is tunable with a single constant per stat, and — critically — it makes stacking *feel* linear even though the percentage curve flattens. Adding a fixed amount of damage reduction always increases effective HP by the same proportional amount, forever. The player sees a number that keeps going up and keeps mattering; the game never divides by zero.

Do the same for cooldowns: store a linear haste-style stat, derive the asymptotic reduction. Items and UI stay readable, and no item combination can ever produce zero cooldowns.

**Candidates for asymptotic derivation:** damage reduction, cooldown, dodge/evasion, lifesteal, slow resistance, move speed (unbounded movement breaks a horde game).

**Candidates for linear:** damage, max HP, regen, pickup radius.

**Watch the deceptive ones.** Area was the classic offender — doubling a radius quadruples the affected area and roughly quadruples damage in a horde game — which is why there is no area stat (§5.5.5). **Projectile count inherits that role.** It reads as linear but compounds against dense clusters where projectiles overlap. Treat it with the same suspicion.

Movement speed also deserves care beyond its asymptote: with no basic attacks, mobility *is* survivability, so speed converts into effective HP more directly than in a game with attack-based kiting.

The real test for any stat is: *what does 100% or infinity of this look like, and does it multiply with itself or with other stats?*

### 5.6.3 Champion-defined growth

XP is a **single team pool** — everyone levels simultaneously and the party is always the same level (§4.1).

Stat gains on level-up are **authored per champion and applied automatically**. A tank accrues health and damage reduction; a carry accrues damage and haste. Not chosen, not skippable, not random. This is where champion identity lives across a run.

**No ability levelling.** Abilities never rank up. They get stronger only because the stats they scale from grew.

### 5.6.4 Stats are purchasable

Gold buys stats directly at the shop when items are unwanted or unaffordable. This gives currency a **floor value** — gold is never dead, a bad shop roll is never a wasted visit, and saving up is never a trap.

Price stats to be **slightly less efficient than items** per gold. If stats are competitive with items, players buy stats and the entire item and path system goes unused.

### 5.6.5 Abilities scale from stats; stats do not modify abilities

**This dependency runs one way, and keeping it one-way is what keeps the system tractable.**

Stats are plain numbers — health, health regen, damage, damage reduction, haste, move speed, and similar. They carry no behaviour and no tags. **Each ability authors its own scaling coefficients** against whatever stats suit its design.

> *An ability whose damage scales with bonus max HP.*
> *A passive whose projectile count scales with move speed.*
> *A shield that scales with damage reduction.*

The stat sheet never knows what an ability does. The ability reads the stat sheet. This means:

- **Every stat is relevant to someone.** Damage reduction is offensive on the champion authored to scale from it. Move speed is damage on the passive that reads it. No stat is dead weight, without needing to make stats do anything clever.
- **Stats stay a flat, cacheable numeric layer** (§9) — no special cases, no behaviour, no interaction rules.
- **Balance stays local.** Changing a scaling coefficient touches one ability. Changing a stat's behaviour would touch everything, which is why stats have none.
- **Augments remain the only thing that changes behaviour** (§6.1). Stats change magnitude; augments change rules. Keeping those roles separate is what keeps the augment layer meaningful.

> **Discrete quantities scaling from continuous stats need explicit thresholds.** "Projectile count scales with move speed" cannot mean 2.7 projectiles. Author it as a step function — a projectile per N move speed — and display the next breakpoint in the UI, or players cannot reason about whether the next purchase does anything.

## 6. Synergy model

### 6.1 Tags

**Everything gets tags.** Champions, abilities, items, augments. Four axes:

- **Source:** ability, **automatic-cast**, projectile, damage-over-time, summon, zone, aura, proc-on-hit (on *ability* hit — there are no basic attacks, §5.5.2)
- **Effect:** shield, heal, stun, slow, root, knockup, mark, dash, blink
- **Cast cost:** free, slowed, rooted (§5.5.4)
- **Scaling:** health, cooldown/haste, projectile count, duration, status effect potency

Tags live on abilities, items, augments, and champions — **not on stats**, which are behaviour-free numbers (§5.6.5). Paths are built from items and augments.

No area or range stat (§5.5.5).

With damage unified (§5.6.1), the **Source axis carries build identity** — paths are built around *how* you deal damage, not what type it is.

**Augments are written as rules over tags, never over specific champions.**

> Not: "Champion X's third ability deals more damage."
> Instead: "Whenever you apply a shield, the shielded ally's next ability deals bonus damage."

That augment now works with every shielding champion, every shielding item, and every future augment that grants shields — including content written two years from now. You author linearly and get quadratic interactions. This is how Slay the Spire, Hades, and Balatro achieve depth on small content budgets. It is the single most important architectural decision in this document.

**Paths are tag clusters the game explicitly recognizes.** Hold three items or augments tagged `projectile` → cross a threshold → unlock a bonus. The UI must make path progress legible. Seeing you are two-thirds into a path is what makes committing a decision rather than a guess.

**Three mechanisms make commitment safe despite randomness (Pillar 3). Use all three:**

1. **Pool weighting.** Once a player holds two items of a tag, that tag appears more often in their offers.
2. **Conversion.** Unwanted items convert to currency for targeted shop purchases. A bad drop is never dead weight.
3. **Trading.** Five players drafting collectively from a random pool is a far richer problem than one player praying.

**Roles are emergent, not a hard class.** A "support" is a champion whose kit is heavy on shield/heal/mark tags. This delivers the "can be multiple roles" requirement without a multiclass system. Team-level synergies are threshold effects over the *union* of everyone's tags — e.g. three or more shield sources on the team causes shields to chain. Rewards composition without forcing one-of-each.

### 6.2 Keywords

A **keyword** is a named mechanic that many pieces of content reference generically. Automatic Cast (§5.5.2) is one. They are the layer above tags: where a tag describes what a thing *is*, a keyword is a small system that items, augments, champions, and paths all plug into.

Keywords are where the content multiplication actually happens. Ten items and ten augments that all reference one keyword produce far more interesting builds than twenty pieces of isolated content, and each new piece added later interacts with everything already there.

**The Arena "Curse" family is the model worth studying**, because its structure is cleverer than it first appears. Several different augments each apply Curse stacks through a *different* action — abilities hitting enemies, attacks, immobilising enemies, or healing and shielding — and each grants a *different* permanent payoff that accrues every second enemies stay Cursed, scaling with stack count. One entry grants offensive power, another resistances, another haste.

Three properties make that work, and they generalise:

**Many entry points, keyed to playstyle.** The door you come in through is whatever you were already doing. A support enters through healing, a bruiser through crowd control, a caster through ability hits. Nobody has to change how they play to participate.

**Different payoffs per entry point.** The support's door grants haste, the bruiser's grants resistances. This *reinforces* role identity rather than homogenising it — the shared keyword does not make everyone play the same.

**Compounding accrual.** Stacks build, the benefit is permanent and scales with stack count. Commitment pays off superlinearly, which is Pillar 3 expressed as a mechanic.

### 6.3 Designing keywords for five players

**This is where a co-op game diverges sharply from Arena, and it is the main risk.**

A shared-target keyword like Curse scales with the number of people applying it. Arena is 2v2; this game is 1–5. Five players stacking a debuff on the same enemies compounds far beyond what one player achieves — often superlinearly, since payoff scales with stack count *and* the number of contributors.

Two consequences:

- **Tune per party size, or cap contribution.** A keyword balanced for five is dead weight solo; one balanced for solo trivialises five-player runs. Either normalise the accrual rate by party size, or cap stacks so additional contributors have diminishing effect.
- **That same property is the opportunity.** A keyword where five players enter through five different doors and all benefit from the shared pool is Pillar 1 expressed as a system rather than a bonus. This is probably the single strongest coordination mechanic available to the design, and it deserves to be the team-facing keyword rather than an individual one.

**Anatomy of a keyword worth building:**

| Element | Requirement |
|---|---|
| Entry points | 3+, each keyed to a different playstyle or role |
| Payoffs | Differ by entry point, reinforcing role identity |
| Accrual | Compounds with commitment |
| Tag | Its own entry on the Source or Effect axis |
| UI | Its own persistent display — stacks, timers, pet count |
| Cap | Explicit, for both balance and performance |
| Party scaling | Normalised or capped |

**Budget roughly five to eight keywords for the whole game.** Fewer and builds converge; more and no single keyword accumulates enough supporting content to feel like a system. A keyword with three items and two augments referencing it is not a keyword, it is a gimmick.

**Candidate keywords for this design:**

- **Automatic Cast** — established (§5.5.2). Entry via weapon items, enablers like Storm Cast, and augments.
- **Pets / Summons** — persistent allied units. Entry via abilities that summon, items that spawn, and augments that convert other things into pets. Payoffs vary: pets that taunt, pets that inherit your Automatic Casts, pets that scale with your stats. **Requires a hard cap** — five players × N pets × 2000 enemies is a pathfinding and simulation problem, not just a balance one, and it lands directly on the §11 horde budget.
- **A stacking debuff** — the Curse analogue. Best candidate for the explicitly team-facing keyword, per above.

**Keywords must cross-reference.** *"Your pets also fire your Automatic Casts."* That single augment ties two keywords together and is worth more than a dozen isolated effects. Design the keyword set so these bridges are possible — that is what makes the whole system feel like one game rather than several bolted together.

> **Trap:** keywords compete for the same content slots. If every item references a keyword, the item pool fragments into disconnected sub-pools and a player who commits to one keyword finds two-thirds of drops useless. Keep a substantial fraction of items keyword-agnostic — pure stats and generic weapons — so every drop has baseline value regardless of build.

### 6.4 Enemies and elites use the same systems

**Enemies are built from the same ability system as champions** (§8) — the same `AbilityDefinition` assets, the same effect steps, the same cast-cost property, the same keyword tags. An elite that channels, dashes, or applies a stacking debuff is authored exactly like a champion ability.

This is a content-velocity decision more than a design one. A new effect step written for a champion is immediately available to enemy designers, an enemy behaviour is available to champions, and both benefit from the same debug trace tooling (§Phase 10). Building a second, parallel enemy behaviour system is a common and expensive mistake.

Trash mobs are the exception: they run on the horde path (§11) with no ability instances at all, because 2000 of them cannot each carry a context object. **Elites and bosses use the full system; trash does not.** That boundary should be explicit in code, and an enemy should be authored as one or the other, never both.

#### 6.4.1 Elite scaling

Elites scale over the run, but **on a gentler curve than the swarm**. The swarm scales in density; elites scale in capability.

Three levers, in preferred order:

1. **Composition** — later elites are drawn from a stronger pool. Cheapest, most legible, most varied.
2. **Modifiers** — stacking affixes that add abilities or properties to an existing elite. This is where the keyword system (§6.2) pays off a second time: an elite that applies the same stacking debuff players can build around, or that spawns its own summons, reuses content already written.
3. **Stats** — flat health and damage growth. Use least. Elites that are simply spongier are the least interesting form of difficulty, and past a point they stop being a threat and start being a chore.

**Elites are where difficulty becomes readable.** A denser swarm feels like the same problem scaled up; a new elite with a telegraphed channelled slam feels like a new problem. Since the endgame shifts toward mitigation (§3.1), late elites should demand exactly that — sustained pressure the team must survive rather than avoid.

> **Telegraphs must scale too.** An elite's wind-up that is legible against 200 enemies can be completely invisible against 2000. Late-game elite telegraphs need to read *through* a dense horde: distinct colour, ground decals that draw over crowds, audio cues. This is a real problem that only appears in late-run playtesting, so look for it deliberately.

## 7. Coordination mechanics

**Proximity effects are one tool, not the answer.** An aura that rewards standing near an ally is fine on a specific champion, item, or augment, and it creates useful positional tension against builds that want to spread out. The failure mode is making it the *primary* coordination mechanic — then coordinating means standing still, which fights the genre. Use it deliberately and sparingly; the mechanics below should carry more weight.

**Combo detection.** Enemies affected by crowd control take amplified damage from *other* players. Tank knocks up, mage detonates. Rewards timing, not positioning. Fire distinct audio and a shared resource tick when it lands.

**Team synergy meter.** Fills from combos and cross-player interactions; spends into a brief team-wide power window. Makes coordination visibly payoff.

**Cross-player augments.** Augments that only function through teammates. Arena flirts with these and they are the most interesting part.

**Positional tension.** AoE clear wants you spread; combo damage wants you close. Real positioning decisions instead of a conga line.

---

# Part II — Architecture

## 8. Ability system

Abilities cannot be methods (`OnQ`, `OnW`) because augments must reach inside an ability's execution and modify it without knowing which champion it belongs to. That requirement dictates the entire shape. Vocabulary borrowed from Unreal's Gameplay Ability System, which solved this problem.

### 8.1 Three-way split

**`AbilityDefinition`** — ScriptableObject. Pure immutable data: costs, base cooldown, tags, targeting mode, ordered list of effect steps.

**`AbilityInstance`** — plain C# class. Runtime state: current cooldown, charges, owner, stacks. Created from the definition at spawn.

**`AbilityContext`** — mutable, created fresh per cast. Carries caster, targets, point, damage packet, mutable tag set, and a reentrancy depth counter.

> **Critical:** never store runtime state in the ScriptableObject. SO assets are shared across every instance — two players on the same champion would share a cooldown timer.

### 8.2 Effect steps

Small ScriptableObjects implementing a single `Execute(context)` method: DealDamage, ApplyShield, Knockup, SpawnProjectile, ApplyStatus, Dash, SpawnZone, Heal, ApplyMark, and so on.

An ability is an authored list — `[Cone Targeting] → [ApplyKnockup] → [DealDamage]` — composed in the inspector without new code.

**Target roughly 30–60 meaningful effect types.** Do not atomize into a full node graph with math nodes; past a point, authoring becomes worse than writing C#.

### 8.3 Slots, not letters

Abilities live in an **indexed array**, not named Q/W/E/R fields. This makes "your third ability is replaced with X" a one-line change and keeps slot handling uniform.

Each slot carries a **role tag** — `passive`, `primary`, `secondary`, `mobility`, `ultimate` (§5.5.3). The fixed five-slot layout is a design convention expressed through these tags, not a hardcoded structure. Augments query the role, so *"your mobility ability leaves a damaging trail"* works on every champion without naming one.

### 8.4 Hook pipeline

Passives and augments are the same thing: objects that subscribe to pipeline stages and mutate the context. Each declares a hook point (PreCast, DamageCalculated, OnHit, OnKill, OnShieldApplied…), a priority, a tag-query match test, and an apply method.

**Priority ordering is mandatory.** Flat additions must resolve before multipliers, or two augments picked in different orders produce different results — and players will notice.

## 9. Stat system

Never store a final number. Store base values plus a list of modifiers; recompute on a dirty flag.

**Fix the operation order once and document it:**

```
(base + flatAdd) × (1 + Σ percentAdd) × Π (1 + percentMult)
```

Percent-add stacks additively; percent-mult stacks multiplicatively. Getting this wrong means rebalancing every item in the game later.

**Every modifier carries a source reference**, enabling clean bulk removal. This is what makes temporary buffs, unequipping, and augment removal trivial rather than a bug farm.

## 10. Networking

**Stack:** Mirror. FizzySteamworks transport for Steam friend-invite P2P; KCP transport for direct/dedicated connections. **Transport is selected at runtime — never hardcode it.**

**Authority model:**

| Domain | Authority | Rationale |
|---|---|---|
| Player movement | Client | PvE — cheating only affects the cheater's own lobby. Removes the need for prediction and reconciliation entirely. |
| Damage, deaths, drops, progression | Host | Consistency where it matters |
| Trash mob positions | Client-simulated from shared seed | Bandwidth |
| Elites, bosses, players | Fully replicated | Precision required |

**Do not replicate trash mob transforms.** Clients simulate movement locally from a shared spawn seed; the host decides what actually dies. A client seeing a mob half a metre off from the host's version is unnoticeable. This is the difference between five players working and five players lagging.

Replicate the **stat modifier list**, not final stat values — each client computes locally, keeping derived stats correct on everyone's UI without constant syncing.

## 11. Horde architecture

The highest technical risk in the project.

- No Rigidbodies. No per-enemy colliders against each other.
- Spatial hash grid for separation, targeting, and queries.
- GPU instancing. No Animator components — animate in shader or via simple transform hierarchies.
- Consider DOTS/ECS for the horde specifically while keeping players as regular GameObjects. Hybrid is common and far less painful than full DOTS.

Blocky art is load-bearing here, not just an accessibility choice. Skinned meshes at these densities will not survive.

## 12. Presentation separation

**No effect step ever instantiates a particle system or plays a sound.** Steps emit gameplay events; a separate presentation layer subscribes and spawns visuals.

This is required by the netcode: the host runs the pipeline authoritatively, but clients must see their own casts instantly. If logic and VFX are fused you cannot run one without the other — and you will be untangling it at the point where it is most expensive.

Enforce via assembly definitions (§13.1).

## 13. Foundation decisions

Cheap now, painful later. All belong in Phase 1.

### 13.1 Three-way assembly split

`Shared` / `ClientOnly` / `ServerOnly`. Gameplay logic lives in Shared and must never reference `UnityEngine.UI`, particle systems, or `AudioSource`. The asmdef boundary enforces this at compile time — the only enforcement that holds. This is what makes a headless build possible later instead of a two-month cleanup.

### 13.2 Stable string content IDs

Every piece of content gets a stable string ID (`augment.shield_amp`), routed through a central content registry. Never Unity GUIDs or direct object references across the wire. Required for networking (host tells clients an ID), save data, and version compatibility. Resolve IDs to network indices once at session start rather than sending strings per event.

### 13.3 External tuning config

Difficulty curves and balance constants in external config files, not baked into the build.

### 13.4 Runtime transport selection

See §10. A dedicated server on a VPS has no Steam client attached; Fizzy cannot serve it.

### 13.5 IL2CPP

Chosen for horde performance. Note that BepInEx and Harmony work on IL2CPP regardless — PEAK shipped with no mod support and was modded anyway. **No modding API is planned.** The properties that make a game accidentally moddable (data-driven content, stable IDs, logic/presentation separation) are the same properties that make it maintainable, and they are already required above. Ship the game; formalize nothing unless a scene emerges.

---

# Part III — Build Order

Two rules drive the sequencing: **retire technical risk before building content on it**, and **reach a playable loop early enough to test with real people.**

Phases 1–8 constitute a real game — a co-op survival shooter with no progression. If only a fraction of this list ships, that is the fraction that must be good.

---

## PHASE 1 — Foundation

### 1. Project setup
- Unity 6 LTS, URP, Git + LFS
- **Three-way assembly definitions** (§13.1) — from day one; they keep compile times sane and force clean dependencies
- New Input System
- ScriptableObject and folder conventions
- **Content registry with stable string IDs** (§13.2)
- **External tuning config** (§13.3)
- IL2CPP build target

**Exit:** builds clean, asmdef boundaries enforced, a placeholder content ID resolves through the registry.

### 2. Character controller and camera
One champion, no network, no combat. **Direct WASD movement with independent aiming, Supervive-style** (§5.5.1) — not click-to-move. Movement feel, dash, camera framing and aim readability.

Get this **genuinely good**. With no basic attacks, movement *is* the moment-to-moment gameplay — it is the thing players touch for sixty consecutive minutes, and it is cheap to iterate now and expensive later.

Prototype the mobility ability here too, not later. It is on every champion (§5.5.3) and its feel sets the game's pace.

**Exit:** moving and dashing around an empty scene is satisfying on its own.

### 3. Networking foundation
Mirror, Steam lobby, invite flow, five players moving in a scene. **Runtime transport selection** (§13.4). Client-authoritative movement (§10).

Do this **now**, while the codebase is small. Retrofitting multiplayer is the most common way projects like this die.

**Exit:** five machines, one lobby, everyone moving smoothly, join and leave handled.

---

## PHASE 2 — Core combat

### 4. Stat system
`StatSheet`, modifier stacks, removal by source, dirty-flag caching, fixed operation order (§9). Replicate modifier lists, not values.

Implement the **store-linear/derive-asymptotic** split (§5.6.2) now, before any content depends on stat behaviour. Unified damage and unified damage reduction (§5.6.1). Tag every stat so it feeds path thresholds later (§4.1).

**Exit:** a temporary buff applies, displays correctly on all clients, and removes cleanly.

### 5. Health and damage pipeline
One funnel every point of damage passes through, taking a damage packet. Shields, resistances, damage types, death state.

Build as a **single choke point** — every augment ever written hooks here.

### 6. Ability system core
Definition / Instance / Context split, effect steps, indexed slots with **role tags** (§5.5.3), **aimed** targeting modes, cooldowns (§8). Networked: client fires visuals and predicted hits immediately, host resolves damage (§5.5.1).

Build one champion with the full five-slot layout — passive, primary, secondary, mobility, ultimate — against a dummy target.

Author **cast cost** (free / slowed / rooted / channelled) as a tagged per-ability property from the start (§5.5.4) — it is how kiting and role differentiation work, and retrofitting it means revisiting every ability.

Implement **per-ability scaling coefficients against stats** (§5.6.5). The stat sheet must stay behaviour-free; abilities read from it, never the reverse. Include a step-function case (discrete quantity scaling from a continuous stat) so the thresholds and their UI are solved early.

**Exit:** an ability is authored entirely in the inspector from existing effect steps, and it works in multiplayer.

### 7. Horde architecture ★ RISK GATE
Spatial hash grid, no rigidbodies, GPU instancing, shader/transform animation, client-side movement simulation from shared seed, host-authoritative deaths (§11).

> **GATE: 2000 enemies · 5 players · 60fps on the low-end target machine, using placeholder cubes.**
>
> Include a **projectile and summon load** in this test: five players × six item weapons firing continuously, some with wall collision, plus pets if that keyword survives (§5.5.2, §6.2). Pets are the worst case — they pathfind, target, and persist. Item weapons multiply by party size the same way enemies do, and bouncing projectiles need collision against level geometry — this is a second density problem layered on the first, and testing enemies alone will give a falsely optimistic result.
>
> If this fails, change architecture **here** — not after two hundred augments are built on top of it.

### 8. Wave director and XP loop
Spawning, difficulty curve, XP pickups, **team-pooled XP** with simultaneous levelling, **automatic champion-authored stat growth** with no player choice (§4.1, §5.6.3). Scale density aggressively, stats gently (§3). **Floor entity despawn timers** from the start — retrofitting them means chasing leaks at density.

Elites come at step 19 with the ability system in place; keep the trash/elite boundary explicit here (§6.4). Augments come at step 11. Enough for a ten-minute survival stage.

> **PLAYTEST GATE 1 — solo and 2-player.**
> Is moving and killing things fun with zero progression systems? If not, no amount of augments will fix it.

---

## PHASE 3 — Systems that make it your game

### 9. Tag system, keywords, and hook pipeline
Formalize tags across champions, abilities, items, augments (§6.1). Define the **keyword set** here (§6.2) — five to eight, each with entry points, differentiated payoffs, a tag, a cap, and party-size normalisation. Keywords authored later than the pipeline end up bolted on rather than integrated. Implement the triggered-effect interface, hook points, priority ordering, and the **reentrancy depth guard**.

> Augments that trigger on damage and deal damage will loop. Every triggered effect needs an internal cooldown, and the context depth counter must refuse to process past a limit and log loudly when it does.

### 10. Debug trace tooling ★
Per-cast breakdown: what triggered, in what order, damage value at each stage.

Build this **immediately** after the pipeline exists. Everything from here is unfixable without it — a five-augment interaction producing a wrong number is genuinely undebuggable otherwise. This tool pays for itself within a month.

### 11. Augment system
Mixed milestone triggering (level thresholds + elite kills + time markers), **full global pause 15–20s**, live teammate hover visibility, network sync of picks, tag-query matching, auto-pick on timeout. Budget ~6 per player per run (§4.2).

**Per-player bag drafting** (§4.2.1): draw 3, one independent reroll per slot, everything seen leaves the bag permanently, reset only on exhaustion. Build this as shared infrastructure — items use the same system with a smaller pool where resets are expected.

Author ~20 augments to stress the pipeline. Include **multiple entry points into at least one keyword** with differentiated payoffs (§6.2), at least one **keyword bridge** ("your pets also fire your Automatic Casts"), and a **cross-scaling** family (stat X applies to system Y at Z% effectiveness) — these are the augments that let players bridge paths, and they exercise the tag system harder than single-system augments do.

> Author some tags as expensive-to-trigger versus cheap, or the highest-frequency source tag (likely `automatic-cast`) will dominate everything. Automatic Casts fire without input and stack across six item slots, so they are the natural runaway.

### 12. Items, currency, shop
All three item shapes — weapon, enabler, stat (§5.5.2). Host-authoritative spawn and damage with client-side visual simulation. **Slot cap (~6)**, a starting item for every champion, and **selling items back for currency**.

**Reuse the bag drafter from step 11** (§4.2.1) with a smaller pool and expected resets. Add **stat selection as a purchase** (§4.1) and bare purchasable stats priced below item efficiency (§5.6.4).

Shop marker with **majority-in-radius trigger**, global full pause, closing timer with early ready-up exit, **dead players excluded** (§4.3.1). Individual wallets, gifting, **player-to-player item trading**, pool weighting toward committed tags.

Networking a full pause with five clients is fiddlier than it looks — pause entry/exit, late joiners, disconnects mid-pause, and the timer all need to be host-authoritative.

### 13. Path thresholds
Tag-cluster detection, threshold bonuses, and the UI that makes path progress legible. This is what turns items from loot into decisions.

### 14. Death and revive
Team respawn charges, **finite map anchors** seeded at run start with clear used/unused map marking, proximity-hold activation reviving all dead players, slow channel revive, purchasable self-revive, last-stand buff (§5).

**Purchasable anchors as deployable items** — occupy an inventory slot when carried, free the slot on deployment, then identical to map anchors. Tradeable at shops and Rest Stages. No auto-drop on death.

Networking deaths and revives is fiddly. Budget more time than feels reasonable.

### 15. Coordination layer ★
Combo detection (CC'd targets amplified by *other* players), team synergy meter, cross-player augments, team-wide tag thresholds (§7).

This is the differentiator. It deserves real iteration time rather than being tacked on.

> **PLAYTEST GATE 2 — 4–5 real players, one full 40-minute run.**
> The first time you learn whether the coordination systems actually produce conversation or get ignored. Watch whether people talk. That is the metric.

---

## PHASE 4 — Structure and meta

### 16. Siege events and Rest Stages
Escalation trigger, siege swarm spawning, **three simultaneous structures** at separate locations with independent progress and health, enemy targeting of all three, visible decay when undefended, first-to-complete fires and collapses the others, all-fall handling (swarm persists, spawn rate drops, run continues).

**Pre-swarm planning window** with all three reward previews visible (§4.4). Tune structure durability **generously** — rotating between structures mid-event must be viable, or the mechanic loses its planning layer entirely.

Then the Rest Stage: saved reward global at full power, each player individually picks one of the two **lost** structures' rewards at reduced power (Pillar 4), ready-up rather than timer.

Author pact-style offers with real costs, not straight upgrades — otherwise the choice collapses into "whichever is closest."

> This step has the most emergent-behaviour risk in the project. Expect the first playtest to reveal that teams either always split (durability too high) or never rotate (durability too low). Budget iteration time on that one number.

### 17. Boss framework
Phase state machines, arena mechanics, telegraphs, networked boss state.

Every run ends at the same final boss, so this boss is the payoff for the entire session and carries a lot of weight. It gets more polish than any other single piece of content.

Still build the framework generally rather than hardcoding one fight — you will want additional bosses post-launch, and endless mode may want mid-run bosses.

### 18. Persistence and account progression
Save data, achievement condition tracking in **your own save data**, **HMAC-signed local file** plus **host-side loadout validation on join** (§4.5.1), points currency, loadout slots. Keep the save layer behind an interface so a backend swap stays cheap.

Cosmetics are out of scope for now.

Rune bonuses are marginal by design (§4.5.2) — a stat seed, a little extra gold. Their value is enabling an earlier start on a synergy path, not power.

**Tune difficulty on party size alone.** One curve per player count; runes do not enter the tuning model.

Endless mode toggle — separate mode, no final boss, unbounded escalation.

> **Acceptance test:** a fresh account, playing **solo**, can complete a run.

### 19. Content scale-up ★ ARCHITECTURE GATE
> Build the next three champions **deliberately unlike each other**: a summoner, a projectile/zone controller, and a mobility-centric shield-support.
>
> All five slots (§5.5.3) must be authored for each, including a mobility ability that does something beyond repositioning, and each must have a **late-game answer** that is not evasion (§3.1).

Author elites here too, on the same ability system (§6.4) — an elite that channels or applies a keyword stack proves the system generalises past player characters.
>
> **GATE:** if the ability system handles all three without bespoke C#, it is sound. If it does not, fix it **here** — with four champions, not twenty.

Then mass-author augments and items. The first time a bespoke C# ability is needed, add a **general effect step**, not a special case — special cases are how these architectures rot.

Build a **headless balance simulator**: thousands of automated runs to find broken combinations. You will not find them by hand.

---

## PHASE 5 — Ship

### 20. Full 5-player UX
HUD scaling to five, ping and comms, reconnection handling, host disconnect behaviour.

> Decide early whether host migration is attempted. It is expensive. Ending the run may be acceptable.

### 21. Performance pass
Real hardware, real content loaded, not placeholder cubes.

### 22. Steam integration
Cloud saves, rich presence, build pipeline, crash reporting, telemetry.

**Achievement mirroring** — push completions tracked in Phase 18 to Steam for display, friend comparison, and storefront badges. One-directional: Steam is an output, never an input (§4.5.1). Progression must remain fully functional with Steam absent.

### 23. *(Optional)* Self-hosted server files
Unity Dedicated Server build target, config file, direct-connect option alongside the Steam friend list. A few days of work given Phase 1's transport selection and assembly boundaries — not a subsystem.

Optionally register with the Steam server browser via the **Steam Game Server API** (anonymous game server logon), giving server listing and player auth without the server needing a logged-in Steam account.

> **Two caveats.** The join UX is harder than the server — Steam's relay hides NAT traversal and identity; direct connect means someone opens a port and shares an IP, and the support burden lands there. And self-hosting earns its keep mainly for *persistent* servers; a 60-minute session with a defined arc means a dedicated server is really just a lobby that does not need the host present.
>
> **Ship the friend-invite flow first. See whether anyone asks. Keep the architecture ready to say yes quickly.**

Add a server config flag for basic server-side movement validation (speed and teleport bounds) on public servers — cheap if movement has one entry point, and client-authority reasoning weakens with strangers.

### 24. Closed playtests → Early Access

---

# Part IV — Reference

## Critical gates

| Gate | Phase | Failure means |
|---|---|---|
| 2000 enemies @ 60fps | 7 | Change horde architecture before building on it |
| Playtest 1 — is it fun bare? | 8 | Fix core loop; progression will not save it |
| Playtest 2 — do people talk? | 15 | Coordination layer needs rework |
| Three unlike champions, no bespoke code | 19 | Ability system needs generalizing at four champions, not twenty |
| Fresh account completes a run solo | 18 | Rune budget too large, or difficulty tuned against upgraded accounts |

## Known traps

- **Reentrancy** — augment loops. Depth guard + per-effect internal cooldowns. (§8.4, Phase 9)
- **Undebuggable interactions** — build the trace tool before you need it. (Phase 10)
- **Content velocity rot** — adding a champion must eventually require zero code. Track it. (Phase 19)
- **Runtime state in ScriptableObjects** — shared across instances; two players on one champion share a cooldown. (§8.1)
- **Operation order in stats** — fix and document once, or rebalance everything later. (§9)
- **Priority ordering in hooks** — flats before multipliers, or pick order changes outcomes. (§8.4)
- **Dominant tag** — the highest-frequency source tag will eat the game unless triggers are costed. (Phase 11)
- **Keywords tuned for one party size** — shared-target keywords compound with contributor count; balanced for five they are dead solo. Normalise or cap. (§6.3)
- **Too many keywords** — under five to eight, none accumulates enough supporting content to feel like a system. (§6.2)
- **Every item referencing a keyword** — fragments the pool so committed players find most drops useless. Keep many items keyword-agnostic. (§6.3)
- **Uncapped pets** — five players × N pets × 2000 enemies is a pathfinding problem, not just a balance one. (§6.2)
- **Unbounded shop pause** — the closing timer is the anti-frustration mechanism; ready-up may only end it early, never extend it. (§4.3)
- **Locking out players outside the shop radius** — majority triggers the pause, but everyone shops. (§4.3)
- **Straight-upgrade tower offers** — if all three are upgrades, the choice collapses to "whichever is closest." Offers must be pacts with real costs. (§4.4)
- **Structure durability** — too high and teams split and hold two; too low and rotating is impossible. The single most iteration-hungry number in the game. (§4.4)
- **Capping asymptotic stats instead of deriving them** — store linear, derive bounded, or items stop feeling meaningful at the cap. (§5.6.2)
- **Projectile count and item cadence** — read as linear, compound against dense clusters, and multiply by party size. The multipliers to watch now that area is gone. (§5.5.5)
- **Uncapped item slots** — five players with unlimited Automatic Casts is a performance problem and makes trading costless. (§5.5.2)
- **Per-frame passive auras** — five continuous radius queries against 2000 enemies. Tick them, don't poll them. (§5.5.3)
- **Long rooted casts** — in a dense horde a long root is a death sentence, not a decision. Keep them sub-second. (§5.5.4)
- **Uniform cast cost** — if every ability is free to cast, kiting has no cost and no skill expression. (§5.5.4)
- **Starting with zero items** — the weakest point of the run is also the first impression. Grant one. (§5.5.2)
- **Open arenas** — geometry-dependent items (bouncing, ricochet) swing wildly in value with obstacle density. Arena layout is a balance surface. (§5.5.2)
- **Haste dominance** — if haste also drives item cadence it becomes universal and eats the stat model. Prefer a separate cadence stat. (§5.5.2)
- **Champions without mobility** — with no basic attacks, spacing comes only from movement. Every champion needs a mobility slot. (§5.5.3)
- **Stats priced competitively with items** — players buy stats, the item and path systems go unused. (§5.6.4)
- **Free failure on siege events** — if losing the tower costs nothing, attempting is always optimal and the decision is fake. The surviving swarm is the price. (§4.4)
- **Stats that modify abilities** — the dependency runs one way. Abilities read stats; stats never carry behaviour. (§5.6.5)
- **Discrete scaling without thresholds** — "projectiles scale with move speed" needs a step function and a visible next breakpoint. (§5.6.5)
- **Shared augment bag** — one player can burn 36 augments a run; five sharing a bag needs 180. Bags are per-player. (§4.2.1)
- **Augment pool below ~60** — the never-see-it-twice promise breaks and late offerings degrade. (§4.2.1)
- **Pure-evasion champions** — mobility stops scaling once the swarm is dense. Every champion needs a non-evasion late-game answer. (§3.1)
- **Cheap item selling** — a low loss makes selling a free build reshuffle and kills commitment. (§4.3)
- **No floor despawn timers** — ground loot and corpses at horde density is a performance leak. (§4.3)
- **Anchors unmarked on the map** — a spent anchor the team does not know about turns a comeback into a surprise loss. (§5)
- **A parallel enemy behaviour system** — elites use the champion ability system; only trash bypasses it. (§6.4)
- **Elite telegraphs that vanish in a crowd** — a wind-up legible at 200 enemies can be invisible at 2000. (§6.4.1)
- **Spongy elites** — flat stat growth is the least interesting difficulty and becomes a chore. Prefer composition and modifiers. (§6.4.1)
- **Exponential stat scaling** — produces a sudden unplayable wall. Scale density instead. (§3)
- **Combinatorial testing** — champions × augments × items × 5 players. Needs the headless simulator. (Phase 19)
- **Steam achievements as source of truth** — spoofable, platform-locked, irrevocable. Own the data; mirror outward. (§4.5.1)
- **Runes entering the difficulty model** — tune on party size alone; runes are noise. (§4.5.2)
- **Assuming players will be carried** — a fresh solo account must be able to finish a run. (§4.5.2)
- **Metaprogression touching tags or keywords** — generic stats, economy, and run-start variance only, or builds get decided outside the run. (§4.5.2)
- **Grind achievements** — reward breadth, not quantity, or the game becomes a farming loop. (§4.5.4)

## Legal note

Mechanical inspiration from Arena is fine. Champion names, kits, and visual designs are not. Riot is generally relaxed about fan projects until something is commercial or uses their assets, and then it is not.

**Build original champions from the start.** Do not plan to file the serial numbers off later.

## Scope warning

Champions × abilities × augments × 5-player multiplayer is a combinatorial testing problem that grows fast. The build order above is designed so that the vertical slice — one champion, one stage, singleplayer, no augments — arrives early and each risky assumption is tested before weight is placed on it.

The augment system is where this game lives or dies. Iterate on it long before debugging five-player desyncs.
