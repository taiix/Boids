# 00 — Game Audit: REEF RUN

Audit date: 2026-10-17. Method: read the repository (git history, scripts, scenes, UI, credits, marketing folder,
Steam capsule work) and the project's working notes. **The game was not played for this audit.** No standalone build
exists in the repo or next to it, and a real round needs four people on Steam. Every gameplay claim below comes from
code, UI text and the existing in-engine marketing captures, not from a play session.

Legend: **[V]** verified in the repo · **[I]** inferred from the repo, likely but not proven · **[A]** assumption ·
**[?]** unknown, needs the team.

---

## 1. Identity

| Item | Finding | Evidence |
|---|---|---|
| Title | **REEF RUN** (two words, logotype all caps) **[V]** | `Assets/UI Toolkit/ReefRunMainMenu.uxml`; `Marketing/SteamCapsules/README.md` |
| Unity product name | Still `Boids` (placeholder) **[V]** | `ProjectSettings/ProjectSettings.asset` (`productName: Boids`) |
| Company | `StanMakes.games` **[V]** | `ProjectSettings.asset` (`companyName`) |
| Genre | Online asymmetric multiplayer survival / hide-and-seek, 3 fish vs 1 shark, proximity voice. Fits the "friend-group co-op" (*friendslop*) and "1-vs-many asymmetric" families **[V]** | Main menu: "3 FISH · 1 SHARK · 10:00 ON THE CLOCK", "One of you will be the shark." |
| Engine / tech | Unity 6000.6.2f1, HDRP 17.6, Mirror networking over Steam (FizzySteamworks), Steamworks.NET, Burst/Jobs boids **[V]** | `ProjectVersion.txt`, `Packages/manifest.json`, `Assets/Mirror` |
| Platform | Windows PC via Steam **[I]**. There is gamepad binding code, but no console or Mac/Linux work | `FishController.cs` (gamepad bindings) |
| Steam App ID | **480 (Valve's public "Spacewar" test app)**. REEF RUN has **no Steam app, no store page and no wishlists yet** **[V]** | `steam_appid.txt` = `480` |
| Version label | "v0.4.1 · EARLY ACCESS" is **hard-coded menu text**, not a release **[V]** | `ReefRunMainMenu.uxml` line 38 |

## 2. Core loop (from code)

1. **Lobby:** a host opens a Steam lobby (friends-only or public, up to **4 players**). The host picks the survival
   clock (default 10:00). Everyone readies up. **[V]** `ReefRunLobbyController.cs` (`MaxSlots = 4`), `ReefRunLobbyBrowser.cs`
2. **Roles:** one player becomes the great white shark and three are small reef fish. **[V]**
3. **Fish:** survive the clock. Hide by **blending into a school of up to ~1,000–2,000 AI fish** (the player shrinks
   to school size and swims with it). Eat food pellets to manage hunger. Finish **co-op tasks** that take time off the
   clock. **[V]** `FishFlockBlend.cs`, `FishVitals.cs`, `FoodSpawner.cs`, `TaskBase.cs` (`timeReward`)
4. **Tasks that force talking** **[V]**:
   - *Turtle rescue:* a loggerhead caught in a ghost net. Four ropes, knots in pairs that must be bitten at the same
     time. Each knot's word shows only to the fish at that rope, so the fish must talk. (`TangledTurtle.cs`)
   - *Sequence task:* a two-fish comms task. One fish sees an arrow sequence and the other enters it, swapping roles
     each round. (`NetworkedSequenceTask.cs`)
   - *Trash clean-up:* push trash into sinkholes. Big pieces need two fish pushing together. (`TrashTask.cs`, `PushableTrash.cs`)
5. **Shark:** bite (dash), lunge (long dash), **sonar** (reveals moving fish within 40 m), near-miss snaps and grazes
   that make fish bleed. A caught fish mashes to break free and is swallowed if it fails. **[V]** `SharkAbilities.cs`, `FishPlayer.cs`
6. **The signature mechanic — talking is dangerous:** fish talk over **proximity voice**. A fish talking within the
   shark's earshot makes a **ripple the shark can see**: exact up close and up to 8 m off at the edge. A fish heard
   clearly can be locked on for 2 s, and its HUD warns "SHARK HEARD YOU". **[V]** `ProximityVoice.cs`
   (`ServerHeardBy`), `SharkTargeting.cs`; design note dated 2026-10-15.
7. **Escape:** when the clock runs out, a wall of the arena lifts and every fish is revealed to the shark. The fish
   race for the gap. **The fish win if more than half escape.** **[V]** `MatchManager.cs`, `ArenaWalls.cs`
8. **After death:** spectate. A **dead-only voice channel** for players out of the round. A **ghost hint** (once per
   round, shows the shark to the living fish for 3 s). **[V]** `Spectator.cs`; design note 2026-10-15
9. **Variety:** 8 mid-round world events / reef conditions: current, trawler net sweeping wall to wall, nightfall
   (glowing plankton trails), lightning storm, cold layer, jellyfish swarm, sand slide, red tide. **[V]**
   `WorldEvents.cs`, `ReefConditions.cs` (763 lines), `MatchManager.WorldEventKind`

**Unique selling proposition (USP):** *the shark hears you.* Fish have to talk to complete tasks, and every word
gives them away. No comparable game found in research (see 01) combines proximity voice as a detection mechanic, a
real flocking school to hide in, and a 3-v-1 shark hunt. **[I]**, based on the comparables reviewed. It's a claim to
test with players, not a fact.

## 3. Feature status

| Area | Status | Evidence / note |
|---|---|---|
| Steam lobby, invite friends, public lobby browser, ready check | Built **[V]** | `ReefRunLobbyController.cs`, `ReefRunLobbyBrowser.cs` |
| Online play (Mirror + Steam relay), host-authoritative round | Built **[V]** | `MatchManager.cs` |
| Proximity voice + "heard" ripples + dead channel | Built **[V]** | `ProximityVoice.cs`, `LobbyVoice.cs` |
| Fish movement, school blending, hunger, vitals | Built **[V]** | `FishPlayer.cs` (1,119 lines), `FishFlockBlend.cs` |
| Shark abilities (bite, lunge, sonar, snap, graze) | Built **[V]** | `SharkAbilities.cs` |
| 3 co-op tasks (turtle, sequence, trash) + hold-task template | Built **[V]** | `Assets/TaskFolder` |
| 8 world events | Built **[V]** | `WorldEvents.cs`, `ReefConditions.cs` |
| Results screen, round start/end UI, pause, settings, credits | Built **[V]** | `RoundScreen.cs`, `PauseMenu.cs`, `ReefRunSettingsPanel.cs`, `ReefRunCreditsPanel.cs` |
| Audio: music by phase, 3D SFX, UI clicks | Built **[V]** (third-party CC0 + 1 CC-BY) | `GameAudio.cs`, `Assets/Audio/CREDITS.md` |
| HDRP underwater visuals, caustics, VFX (blood, bubbles, splash) | Built **[V]** | `Marketing/Screenshots/*`, `Assets/VFX` |
| Maps | **One** arena (the island reef) **[V]** | `Assets/Scenes/03 - Island.unity` |
| Gamepad | Partial: bindings exist for movement **[V]**; full UI and controller support unverified **[?]** | `FishController.cs` |
| Tutorial / onboarding | None found **[I]** | no tutorial scene or script |
| Matchmaking with strangers | Public lobbies only; no skill/quick match **[V]** | lobby browser |
| Localization | None (English UI text in UXML) **[V]** | `ReefRun*.uxml` |
| Achievements, stats, cosmetics, progression | None found **[I]** | no achievement or cosmetic code |
| Mac / Linux / Steam Deck | Not addressed **[?]** | HDRP has no Mac/Linux play path the team has tested |

## 4. Known defects and risks found in the repo

| Issue | Severity | Evidence |
|---|---|---|
| **On a pure client, the other player's body has netId 0 and doesn't move. A client-side shark can't eat anyone.** Found 2026-10-15, cause unknown. It may be an Editor/multiplayer-play-mode artefact, but it hasn't been tested in a real Steam session. | **Critical**: breaks the core loop for 3 of 4 role draws if it's real | project note "remote-player-netid-is-zero-on-clients" |
| Performance: Editor baseline ~17.5 ms main thread, GPU 11–14 ms at 1080p. The water surface alone costs ~4–5 ms. Seaweed physics spiked to 12.5 ms (fixed to ~1 ms). | High for a cheap friend-group game: players' PCs vary widely, and HDRP raises the minimum spec | project note "profiling-the-editor-via-cli" (2026-10-16) |
| Seabed regenerated by GPU erosion every run, so coral placement shifts between sessions | Low (cosmetic) | `Marketing/README.md` |
| Startup order trap with Steam bootstrap (fixed) | Fixed | project note "steam-boot-order-trap" |
| "EARLY ACCESS" text in the menu with no store page | Low, but must not appear in campaign footage as a claim | `ReefRunMainMenu.uxml` |

## 5. Assets and licences

| Asset | Source | Licence status |
|---|---|---|
| Great white shark + many fish | "Underwater life deluxe" pack | **[?]** Presumably Unity Asset Store Standard EULA, which allows use in a commercial game. **Check that the pack's licence allows marketing renders** (the capsule README already flags this). |
| Coral | "Coral Reef Decor Pack - HDRP" | **[?]** Presumably Asset Store. Confirm the seller and licence. |
| `Models/RiggedFish.fbx`, `Models/Kelp/KelpForest 1.fbx`, `Models/Seaweeds/SM_Seabed_023_*`, `Models/Trash/*`, FPX_Seabed rock emitters | Unknown origin | **[?] Provenance not recorded. Must be confirmed before any campaign.** |
| Loggerhead turtle, turtle net | Probably in-house (`Blender/Turtle`) | **[I]** Confirm. |
| Music and SFX | Kenney + OpenGameArt | **[V]** CC0, except "Acid burn" (mikhog), CC-BY 3.0, which is credited (`Assets/Audio/CREDITS.md`) |
| Fonts | Barlow / Barlow Condensed | **[V]** SIL OFL 1.1 |
| Networking and SDKs | Mirror (MIT), FizzySteamworks, Steamworks.NET (MIT), Discord SDK, Edgegap plugin, CodenameLib (the team's own GitHub) | **[I]** Standard permissive licences; Discord SDK under Discord's developer terms |
| Steam capsules, screenshots | In-engine renders by the team | **[V]**. No AI-generated or stock imagery (capsule README) |

**Consequence for Kickstarter:** the rules say "Projects must solely contain materials that the creator has rights
to using" (kickstarter.com/rules, read 2026-10-17). Third-party music can't be offered as a "soundtrack" reward,
because rewards must be produced or designed by the project or a collaborator.

## 6. Team and capacity

| Fact | Evidence |
|---|---|
| Active contributors since 2026-09-01: **Stan** (29 commits, also as StanislavV) and **Narniez** (22 commits). **mnnice** has 1–3 small commits. **[V]** | `git log --since=2026-09-01` |
| Real development started **2026-06-26** (the 2024 commits are an empty template). About **16 weeks** from first Steam lobby to the current build **[V]** | `git log` |
| Split of work: Stan on networking, Steam, UI, VFX, vegetation and marketing; Narniez on fish/shark control, boids, tasks, turtles and round UI **[I]** | commit authors per feature |
| Full-time or part-time, paid or unpaid, location of each member **[?]** | not in repo |
| AI-assisted development: the project's working notes show an AI coding assistant doing substantial implementation, marketing-tooling and art-direction work **[V]** | project notes |
| Team location: **the creator (Stan) is resident in the Netherlands** (stated by the user 2026-10-17). The development PC's Windows region is set to Bulgaria, which doesn't decide eligibility. Other members' locations are unknown. | user statement; `Get-WinHomeLocation` |

## 7. Audience and traction

| Channel | Status |
|---|---|
| Steam store page / wishlists | **None.** App ID 480 |
| Mailing list | None found |
| Social accounts (X, TikTok, YouTube, Reddit, Discord server) | **None found in the repo. Assume zero until the team confirms. [?]** |
| Playtesters outside the team | No evidence |
| Press, creators, awards, partnerships | None. **Don't claim any.** |
| Kickstarter account or prior campaigns | None found **[?]** |

## 8. Strengths

1. **A hook you can explain in one line, and that sells itself on video:** "One of you is the shark, and it can hear you."
   Proximity voice makes stream and clip moments (screams, whispered plans, betrayal-adjacent comedy). That's what
   spread *Lethal Company*, *Content Warning*, *R.E.P.O.* and *PEAK*.
2. **Very good visuals for the price band.** HDRP water, caustics and flocking schools beat the low-fidelity look
   that's usual in friend-group co-op. The screenshots read in under a second.
3. **A real, working systemic core:** a loop, tasks, events, an escape finale and spectating are already in place.
   It's more than a vertical slice.
4. **Fast team:** a substantial networked game in about 16 weeks.
5. **Original marketing assets already made in-engine**, with a documented, reproducible pipeline (Tools > Marketing).

## 9. Weaknesses

1. **Zero audience** and no Steam page. That's the single biggest gap for a crowdfunding campaign.
2. **A critical network bug** (client-side shark) is still open.
3. **Exactly 4 players needed.** The game is only fun with a full group, and there are no bots and no matchmaking.
   This narrows the audience to people with three friends on hand, or the public lobby browser.
4. **One map, three tasks.** Replay depth after 5–10 rounds is unproven.
5. **HDRP hardware cost** limits the low-end PCs that friend groups often have.
6. **The team is small and likely unpaid**, with an unknown time commitment.
7. ~~Possible Kickstarter eligibility blocker~~ Resolved 2026-10-17: the creator lives in the Netherlands, an eligible country.

## 10. Missing information (asked in NEXT_ACTIONS.md)

- ~~Country of residence~~ The creator is in the Netherlands. Other members: where they live matters only for how they're paid (contractor invoices).
- Hours per week each person can commit through 2027, and whether anyone needs a salary.
- Licence and provenance of the unknown models (§5).
- Whether the netId-0 bug reproduces in a real Steam session.
- Any existing social accounts, friends-and-family list or Discord.
- Target hardware and whether a lower-quality preset is feasible.
- Whether the team is willing to appear on camera (Kickstarter's Discovery Mode advice favours real people).
