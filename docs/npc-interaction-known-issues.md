# Known issues: casting feedback, NPC interaction and the gossip channel

Everything here was found while building class trainer support, and left unfixed on
purpose. Each entry records the evidence, the mechanism and the intended fix, so it can
be picked up without re-deriving it. Log excerpts are from
`BlazorServer/out20260803*.log` (Wrath 3.4.3, addon 1.12.0).

Ordered by consequence.

---

## 1. `CurrentActionNotDetected` re-casts spells that already landed

**Symptom.** A cast reports failure and the bot presses the same key again seconds later:

```
[Rend] instant input 51ms CAST_SUCCESS -215.5411ms
[Rend] ... Cast failed due CurrentActionNotDetected!
```

Same spell retried at 22:01:56, 22:02:04, 22:02:12, 22:02:18. Every failing line carries
`CAST_SUCCESS`, so the spell did land. `Rend` dominates - it is the only action in the
test profile with `Required Form: Warrior_BattleStance`, which may or may not be related.

**Mechanism.** `CastingHandler.WaitCurrentAction` accepts only two things inside a
budget of `Max(HalfSpellQueueTimeMs, RemainCastMs) + NetworkLatency` (~215 ms measured):

```csharp
bool Interrupt() =>
    currentAction.Is(item) ||
    playerReader.CastState == UI_ERROR.CAST_SENT ||
    token.IsCancellationRequested;
```

* `currentAction` is the addon's `IsCurrentAction` bit, true only briefly for an instant
  spell - the pixel pipeline can sample straight past it.
* `CAST_SENT` is an **intermediate** state. By the time the bot samples, the addon has
  already advanced to `CAST_SUCCESS`, so the terminal state is never matched.

`CastInstant` then times out and, at `CastingHandler.cs:214-228`, already knows better:
`CastInstantSuccessful(playerReader.CastEvent.Value)` is true. That result is used only
to *suppress* a `UIError` return, after which it falls through to
`return CastResult.CurrentActionNotDetected` anyway.

**Why it costs more than a log line.** `Cast()` returns `false` for this result
(`CastingHandler.cs:799-813`), so `item.SetClicked()` and `UpdateGCD()` never run. The
cooldown and charge bookkeeping is not updated and the goal re-presses the spell.

**Intended fix.** Accept the terminal state in the wait, so `elapsedMs` goes positive and
the normal post-cast path runs rather than converting a timeout into a success:

```csharp
(item.SpellId != 0 &&
 playerReader.CastSpellId.Value == item.SpellId &&
 CastInstantSuccessful(playerReader.CastEvent.Value))
```

The `CastSpellId` comparison is load-bearing. `playerReader.CastEvent` is the global cell
62 holding the most recent event for *any* spell; without the ownership check a stale
`CAST_SUCCESS` from the previous cast would satisfy the wait instantly and mask genuine
failures. `CastEventReader` already keeps a per-spellId log for exactly this reason
(`CastingHandler.cs:341-356`) and may be the better source.

**Risk.** This is on every cast the bot makes. Worth landing on its own so combat
regressions are easy to attribute.

---

## 2. `MoveToTargetAndReached` burns 10 s on an NPC already in range

**Symptom.** At a vendor, ten seconds pass between the interact and the goal noticing:

```
23:04:05.598  Interact pressed
23:04:05.8-06.8  merchant window opens, SellJunk sells 13 stacks, +1s 8c
23:04:15.656  "Found Target!"          <- 10s later
```

**Mechanism.** `AdhocNPCGoal.Navigation_OnDestinationReached` runs the soft-interact
branch unconditionally when `bits.SoftInteract()` is set, and that branch calls
`MoveToTargetAndReached()`, which waits up to `MAX_TIME_TO_REACH_MELEE` (10 000 ms) for
`bits.NotMoving`. Standing still at the NPC, the whole budget elapses while the merchant
window is already open and selling.

**Partly mitigated.** The redundant second `PressInteract` that used to *close* that
window is now skipped via `bits.MerchantFrameShown()` / `bits.TrainerFrameShown()`, and a
failed interaction now clears the target instead of walking away still holding it. The
10 s wait itself remains.

**Intended fix.** Skip or shorten the approach when the frame is already up, or when
`playerReader.MinRangeZero()` already holds before waiting.

---

## 3. The gossip channel cannot distinguish fresh data from stale

**Background.** Cell 73 latches - the addon writes only when its queue actually pops:

```lua
local gossipNum = DataToColor.gossipQueue:shift(globalTick)
if gossipNum then Pixel(int, gossipNum, 73) end   -- no `or 0`
```

So the cell holds the last value indefinitely. The queue runs
`GOSSIP_START (69)` -> option hashes -> `GOSSIP_END (9999994)`, and the bot only starts
polling after it presses interact - by which time `69` has long been overwritten.

**What this caused.** `OpenMerchantWindow` and `OpenTrainerWindow` both waited on
`GossipStart()` first. That value was never observed, so the wait always burned its full
`TIMEOUT` (5 s), after which the second wait found `GossipEnd` already latched and
returned instantly. Measured 5.026 s per trainer visit, 10 s for a vendor with a gossip
menu (both 5 s waits expiring).

**Fixed by** collapsing each into a single wait on `GossipEnd() || <frame shown>`.
Trainer visits went from 5.03 s to 0.13 s; the vendor visit to 0.5 s.

**Residual risk, unaddressed.** Because `GossipEnd` also latches, a wait can return
immediately on a value left over from a *previous* NPC and then read a stale
`GossipReader.Gossips`. `GossipReader.Update` only clears that dictionary on
`GossipStart`, so a genuinely fresh menu does reset it - but the window between "we begin
polling" and "the new `GOSSIP_START` arrives" is real. Waiting on `GossipStart` was the
original attempt to guarantee freshness; it simply lost the race every time.

**Intended fix.** A sequence number on the gossip channel, so the reader can tell "this
menu is newer than the one I saw before I interacted" rather than inferring it from a
sentinel value. `CastEventReader`'s cell 113/114 encoded-value-plus-monotonic-seq pairing
is the pattern to copy.

---

## 4. `InitSlot` runs twice for every KeyAction

`KeyActions.InitBinds` calls `keyAction.InitSlot(logger)` (`KeyActions.cs:19`) and
`KeyAction.Init` calls it again (`KeyAction.cs:231`). `ClassConfiguration.Initialise`
runs both loops in sequence, so every action is slot-resolved twice and any warning it
emits prints twice.

**Not simply removable.** `WaitKeyActions.AddNewKeyAction` appends generated Food/Drink
wait entries *after* `InitBinds` has already run for that section, so `Init`'s call is
their only slot resolution. Deleting it would silently skip them.

**Intended fix.** Have `AddWaitKeyActionsForFoodOrDrink` initialise the entries it
creates, then drop the call from `KeyAction.Init`. The duplicate-warning symptom is
already gone - keyless sections are now flagged `KeyOptional` - so this is redundant work
only, not user-visible.

---

## 5. Spellbook lower-rank block is terminated, not counted

`InitSpellBookQueue` sends the highest rank of each spell under a
`QUEUE_COUNT_MARKER + n` header, then the remaining ranks, then
`SPELLBOOK_ALL_RANKS_END`. The first block is counted; the second is only terminated.

A terminator proves the batch *ended*, not that every id in it *arrived*. A dropped value
leaves a hole in `SpellBookReader.spells` while `AllRanksReceived` still flips true, and
`HasExact` reads a hole as "not known" - which `TrainerPlanner` turns into a purchase.

**No evidence this has ever happened.** It was suspected once and disproved: the repeated
purchases in `out20260803_old.log` were a `.unlearn` test macro, not lost pixels.

**Intended fix if pursued.** Send the low block as `marker`, `count`, `ids` - the count
as its own value rather than folded into the marker, since a cell tops out at 16,777,215
and `QUEUE_COUNT_MARKER + n` saturates at `n = 215`, which a level 60+ spellbook exceeds.
A count of zero cannot be sent (zero is what an empty queue writes and the reader
ignores), so the rank-less clients - Cata and MoP dropped ranks entirely - need a
separate "no lower ranks" sentinel. Counting must also tolerate the same id arriving on
two consecutive frames, and must still complete on a `SPELLS_CHANGED` re-send where every
id is already in the set.

---

## 6. Class trainer matching skips Death Knight and Demon Hunter

`UnitClass.TrainerSubName()` returns the plain enum name and the match is a
case-sensitive-insensitive substring test against `Creature.SubName`. That covers every
class trainer in `som` and `tbc` (201/201, 271/271) and all the spell-teaching ones in
`wrath`, but not the two classes spelled inconsistently in the data:

* Wrath writes `Death Knight`, MoP writes `Deathknight` - one keyword cannot match both
  without ignoring spaces.
* **Wrath does not flag its DK trainers as `ClassTrainer` at all.** The eight NPCs whose
  `SubName` is `Death Knight` lack the bit, so no code-side keyword change reaches them;
  it is a `creatures.json` / `Utilities/TransferTrainerFlags` gap.

Unmatched rows in the other eras are `Portal Trainer`, `Pet Trainer`, `Battle Pet
Trainer`, `Demon Trainer` and blanks - none of which teach class spells, so excluding
them is correct.

---

## 7. Profession and weapon-skill training is not implemented

[Issue #671](https://github.com/Xian55/WowClassicGrindBot/issues/671) is titled *Skill
Training*, and "skill" in WoW usually reads as a profession or weapon skill. The first
iteration covers **class trainers only**; `NpcFlags.ProfessionTrainer` is populated and
the navigation half would work unchanged, but the filtering does not carry over - a
profession advances through ranks (Apprentice, Journeyman, Expert...) gated on skill
level rather than through spell ranks gated on character level, so
`TrainerSpells.GetState` does not describe it.

Deferred to a second iteration.

**Weapon Masters are flagged `ProfessionTrainer`.** In som, tbc and wrath every one of
them - `Hanashi`, `Woo Ping`, `Sayoc`, `Buliwyf Stonehand`, `Ilyenia Moonfire` and the
rest - carries the profession bit rather than plain `Trainer`.
`Utilities/TransferTrainerFlags` has a `WeaponTrainer->Trainer` path meant to strip
exactly that, but the committed `creatures.json` does not reflect it (som and tbc have
*no* plain-`Trainer` rows at all; wrath has 34). So a profession-trainer search will also
match weapon masters until the data is regenerated or the goal filters on `SubName`.

Weapon skills themselves - Vanilla/TBC/Wrath only, removed in Cata 4.0 - are a third
shape again: no ranks, nothing to choose, learnt once and then levelled by use. Deferred
further still.

---

## 8. Trainer purchase order is arbitrary

`Trainer.lua` iterates `pairs(mWanted)`, which is unordered, so it may try a higher rank
before the one it depends on. This self-corrects: a rank the trainer will not yet sell is
simply skipped, the prerequisite is bought, and the next tick's rescan picks the higher
one up. Worth knowing it relies on that rescan rather than on ordering.
