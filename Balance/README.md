# Balance (2026-11-17)

Two simulators: minigames on their own (`bots/`), and whole matches, fish against the shark (`match-bots/`).

# Part 1: minigames

## How it was tested

`bots/` is a simulator that copies each minigame's rules and constants from the game code, then plays it
400 times at three skill levels (run it with `dotnet run -c Release` in `bots/`). Raw output: `bot-results.txt`.

| Bot | Timing error | Skipped presses | Stray presses | Reaction |
|---|---|---|---|---|
| sharp | 25 ms | 2% | rare | 0.22 s |
| average | 50 ms | 6% | 0.04/s | 0.30 s |
| sloppy | 90 ms | 15% | 0.10/s | 0.45 s |

It does not cover the shark, swimming between tasks, or talking (finding the matching turtle knots).
The match log covers those in real playtests.

## Results (median seconds of play)

| Task | Reward | Sharp | Average | Sloppy | Average: seconds off per second spent* |
|---|---|---|---|---|---|
| Coral | 15 s | 9 | 10 | 14 | 0.86 |
| Leak | 20 s | 10 | 13 | 129 (bursts) | 0.97 |
| Tangled line | 15 s | 5 | 8 | 16 | 0.94 (bot is faster than people) |
| Turtle net (2 fish) | 45 s | 23 | 33 | 81 | 0.55 per fish |
| Trash bag (1 pt) | 4 s + share of 30 s | 26 | 68 | 165 | 0.10 |
| Trash net ball (2 pt, 2 fish) | 8 s + share | 31 | 97 | 257 | 0.07 per fish |
| Hiding (until out of breath) | | 35 | 32 | 24 | 0.4 / 1.3 / 3.2 twitches per hide |

\* reward divided by (play time + about 8 s of swimming); trash counts 4 s per point plus 30/9 s for its share
of the completion bonus.

## Findings

1. **Trash is the outlier.** The needle is only inside a box for about 40 ms either side of its centre, so an
   average player hits 53% of boxes. A miss costs 0.15 and a hit gives 0.08 to 0.22, so speed sits near 20%
   and one piece takes over a minute for 4 seconds off the clock: roughly 10x worse than every other task.
2. **Pushing the net ball together rarely syncs.** Each fish has its own random boxes, so only 43% of hits
   land within the 0.3 s sync window for average players.
3. **Coral needs no skill.** Nearly everyone finishes in the minimum 9 s. That's fine for an easy task.
4. **Leak and line are fair.** Most players seal the leak in 10 to 18 s; only very sloppy play bursts it repeatedly.
5. **Turtle is fair** on play time; the real cost is finding the matching knot, which the bots don't model.
6. **Hiding lasts 24 to 35 s.** (Perfect tapping gains back some breath, so it's longer than the 28 s drain.)
   Whether that's too strong depends on the shark, so it needs playtests.

## Trash tuning tried (bag, 1 fish)

| Sweep / box width / miss penalty | Sharp | Average | Sloppy |
|---|---|---|---|
| 0.85 / 0.20 / 0.15 (now) | 26 s | 70 s | 174 s |
| 0.85 / 0.30 / 0.15 | 20 s | 33 s | 110 s |
| 0.85 / 0.30 / 0.08 | 20 s | 26 s | 79 s |
| 1.00 / 0.30 / 0.08 | 19 s | 25 s | 70 s |

## Match log

`Assets/Scripts/Gameplay/MatchLog.cs` writes one CSV per match, per machine, to
`%USERPROFILE%\AppData\LocalLow\Unacite Studio\Reef Run\MatchLogs\`.
Columns: `t, who, event, task, seconds, detail`.

| Event | Logged by | Detail |
|---|---|---|
| round_start / round_end | every machine | result (FishWin / SharkWin) |
| start / solved / left | the player | time spent; why they left |
| start / left / sunk (Trash push) | the player | hits, misses, metres moved, together |
| start / solved / left (Turtle net) | the player | cuts, misses, pair progress |
| start / left / gasped (Hide) | the player | twitches, missed beats, breath |
| clock_cut, task_done, trash_sunk | host | seconds off, seconds left |
| fish_died / shark_died | host | seconds left on the clock |

After a playtest, collect the files from every player.

# Part 2: fish against the shark

`match-bots/` plays whole matches on the 300 x 300 m arena (run `dotnet run -c Release` for the
main tables, `-- levers` for rule changes; output in `match-results.txt`). It uses the game's numbers:
both swim 6 m/s (11 boosting); shark lunge 26 m/s for 0.5 s every 4 s; the shark picks out a player
within 30 m (easier at a task than in a school); fish see 31 m; jaws 4.5 s to mash free (break free = -30
health, shark stunned 1.5 s); graze = wound, no escape; hunger for both; clock 600 s minus task rewards;
the exit reveals every fish; more than half must make it.

Guesses (no code to read): how fast players notice the shark, dodge a lunge, mash, lose ground in a
chase, and how long a school fish takes to catch. Doubling or halving each moved the result by a few points.

## Today: the shark wins almost every match

| 4 fish | sharp shark | average shark | sloppy shark |
|---|---|---|---|
| sharp fish | 8% | 19% | 43% |
| average fish | 3% | 4% | 17% |
| sloppy fish | 0% | 2% | 1% |

Average vs average: first catch about 45 s in, about 9 catches a match, 3.2 of 4 fish eaten before the
clock runs out. **61% of catches are a fish caught again within 25 s of breaking free**: the shark is
stunned for only 1.5 s and swims as fast as the fish. Two break-frees leave 40 health, and from there
it takes 9 presses a second to get out, so the third catch almost always kills.

## What moves it (average vs average, 4 fish)

| Change | Fish win |
|---|---|
| none | 4% |
| task rewards x1.5 / x2 | 6% / 12% |
| break free costs 15 health | 6% |
| lunge cooldown 7 s | 6% |
| round 450 s / 360 s | 11% / 17% |
| shark stunned 4 s / 6 s after a break-free | 19% / 23% |
| stun 6 s + round 450 s | 35% |
| **stun 6 s + round 450 s + rewards x1.5** | **41%** |

With that last set: sharp fish win 55% against a sharp shark, average 41% vs average, sloppy 19% vs
sloppy. By player count: 2 fish 19%, 3 fish 49%, 4 fish 38%, 5 fish 68%, 6 fish 71%. Even counts are
harder because more than half must make it (2 of 2, 3 of 4).

Not modelled: voice being heard by the shark, world events (current, trawler), kelp and rocks as cover
beyond a flat sight-line chance, depth. Check these with the match log in real playtests.

## Applied (2026-11-17)

| Change | Where |
|---|---|
| Breaking free dazes the shark for 3 s: it can't swim or bite (its existing stunned state, without the health loss) | `FishPlayer.sharkStunSeconds` = 3 (both player prefabs), `FishVitals.Daze` |
| Round 7:30; the lobby's clock choices are now 5:00 / 7:30 / 10:00 (was 5 / 10 / 15), default 7:30 | `ReefRunLobbyController.ClockOptions`, `ReefRunLobby.uxml`, `MatchManager.roundDurationSeconds`, IslandTest |
| Every task takes 1.5x as much time off the clock | `MatchManager.taskRewardScale` |

Why a daze rather than the old bite-only stun: the game's previous 1.5 s stun only blocked bites,
and the simulator showed that lengthening it does nothing (the shark follows and bites when it ends):
6 s of no-bites with the other two changes gives 13% fish wins, a 3 s daze 41%.
Not yet re-checked: the shark's side of a daze in a real two-player match.

## Applied, part 2 (2026-11-17)

| Change | Where |
|---|---|
| Trash needle: sweep 1.0 s (was 0.85), boxes 0.3 (was 0.2), miss penalty 0.08 (was 0.15) | `TrashMinigameUI` |
| Stations each match: 2 leaks, 3 coral, 2 lines (was 1 / 2 / 1), random spots 55 m+ apart | `MinigameStations.Counts` |
| Turtles: unchanged, IslandTest already frees 3 at random spots; trash already has 34 pieces round 3 random sinkholes | |

Trash per piece now (bots, average): 26 s alone (was 68 s), 37 s for a two-fish net ball (was 97 s; 58% of hits sync).

Matches with the real task list (`match-results-now.txt`; earlier runs counted 1 turtle and 9 trash points):

| Average vs average, 4 fish | Fish win |
|---|---|
| rules this morning | 2% |
| + daze, 7:30 round, rewards x1.5 | 34% |
| + trash fix | 40% |
| + more stations (now) | 38% to 41% |

Now by skill: sharp fish 58 / 69 / 86% against a sharp / average / sloppy shark, average fish 23 / 41 / 63%,
sloppy fish 5 / 7 / 17%. By player count: 2 fish 21%, 3 fish 50%, 4 fish 40%, 5 fish 70%, 6 fish 75%.

## Correction (2026-11-17, evening)

Only the 10 net balls use the push minigame. The 24 bags and scraps are pushed by swimming into them
(about 18 s a piece at any skill), so Part 1's "trash is 10x worse value" applies to the net balls only.
The match simulator now uses that (`match-results-now.txt`). Average vs average, 4 fish: 38 to 41% fish wins;
2 fish 17%, 3 fish 45%, 5 fish 74%, 6 fish 79%.

## Testing shortcuts

Play mode, Tools > Reef Run > Minigames (Ctrl+Shift+1 to 6): moves your fish to the nearest unfinished
net ball, turtle knot (with a pretend partner on the matching knot), leak, coral, tangled line, or the
sand under you, and starts it. Offline as the shark, it swaps you to a fish first.
