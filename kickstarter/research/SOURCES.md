# Source log

Every external fact used in the campaign package, with where it came from, when it was read, and how far to trust it.
Reliability: **A** = primary or official source, **B** = reputable secondary source or analyst, **C** = vendor blog,
anecdote or user-generated content.
Raw pulls: `research/steam_comps.json` (Steam store API), `research/charts.json` (SteamCharts). Both were fetched
2026-10-17 by `tools/fetch_steam_comps.py`.

## Kickstarter platform (all read 2026-10-17)

| # | Source | Key finding | Rel. |
|---|---|---|---|
| K1 | kickstarter.com/help/fees (US, NL, DE, GR, SI, GB variants) | 5% Kickstarter fee + payment processing **3% + $0.30 / €0.30 / £0.30 per pledge**. Pledges under 10 units: **5% + 0.08**. No fees if unfunded. Older third-party blogs still say $0.20, which is outdated. | A |
| K2 | kickstarter.com/rules | Projects must create something new. "All rewards must have been produced or designed by the project or one of its collaborators." AI allowed "as long as there is human creativity and human involvement", and its use must be disclosed. No equity or financial returns. Creators must not "artificially inflate the number of backers", must not "use Kickstarter as a method to funnel engagement to external websites or platforms", and must not spam. Prototype demos must show the product's current state with no CGI for functionality that doesn't exist. | A |
| K3 | help.kickstarter.com/en-us/articles/16236650 (Who can use Kickstarter?), updated 2026-08-06 | Creators must reside in, or have an entity registered in, one of 25 countries: AU, AT, BE, CA, DK, FR, DE, GR, HK, IE, IT, JP, LU, MX, NL, NZ, NO, PL, SG, SI, ES, SE, CH, UK, US. **Bulgaria isn't listed.** Workaround named by Kickstarter: incorporate where it's open, e.g. Stripe Atlas (US). A Bulgarian IBAN may be used only by a creator verified as resident in an eligible euro country. | A |
| K4 | help…/16236380 (create a business in the US) | Stripe Atlas lets non-US founders incorporate in the US and run USD projects. The representative can verify with local ID and doesn't need a US SSN. Approval isn't guaranteed. | A |
| K5 | help…/16236406 (entity verification) | The representative must be an owner or executive and take part in the project. Owners with ≥25% are verified. The representative's name is public. | A |
| K6 | help…/16236742 (duration) | 1–60 days. Kickstarter recommends 30 days or less. The deadline can't be changed after launch. | A |
| K7 | help…/16236740 (media specs, updated 2026-09-30) | Images ≤50 MB, JPEG/PNG/GIF recommended, ≥700 px wide, compressed on upload. Animated GIFs may show as static, so use WEBP. Video ≤5 GB (MOV, MPEG, AVI, MP4, 3GP, FLV, WMV), displayed at 640×360 (16:9). YouTube/Vimeo can be embedded in the story. Alt text is supported. | A |
| K8 | help…/16236743 (video required?) | A video isn't required, but projects with one "tend to succeed at a much higher rate". Optional **Discovery Mode** vertical 9:16 video, 60–90 s recommended, real people over polish, captions advised, ≤5,120 MB. | A |
| K9 | help…/16236802 (captions) | Caption tracks are uploaded as **.VTT**. | A |
| K10 | help…/16236759 (AI) | Disclose what AI is used, how it's incorporated, and which elements are AI output versus original. The answers are shown on the project page. | A |
| K11 | help…/16236379 (pre-launch page) | Available to first-time creators **after the project passes review**. "Notify me on launch" followers. The follower count shows once there are ≥10. Pre-launch updates email followers. Launch is manual and can't be scheduled. Kickstarter recommends going live with the pre-launch page ≥1 week before launch. | A |
| K12 | help…/16236709 (funding goal) | The goal should cover project costs + fees. The test: could you deliver at exactly 100%? Shipping charged at pledge counts toward the total. | A |
| K13 | help…/16236386 (Pledge Over Time) | Backers split a pledge into 3 payments. Paid out at ~0, ~8 and ~12 weeks after the end (for payouts from 2026-01-15). Not available for late pledges. | A |
| K14 | help…/16236542 (Late Pledges) | Kickstarter supports late pledges after a successful campaign. **Conflicts with Steam key rules for unreleased games (S3).** | A |
| K15 | help…/16236640 (digital rewards) | With Kickstarter's Pledge Manager, creators can send files (≤1 GB each, 2 GB per project) or codes/URLs. Backers keep access for 2 years. | A |
| K16 | help…/16236466 (Projects We Love) | Games projects can be submitted to the Games team for feature consideration via a link. | A |
| K17 | help…/16236756 (max reward) | Maximum reward/pledge: US $10,000. Euro countries €8,500. Minimum $1 / €1. | A |
| K18 | help…/16236589 (tax expectations) | The Pledge Manager can collect sales tax or VAT for US, CA, UK and EU backers after the campaign. Customs are separate. | A |
| K19 | help 360010120934 (payout timing, via search snippet) | 14-day collection window, then payout. The bank adds 3–14 business days. | A (snippet) |
| K20 | updates.kickstarter.com/a-year-in-review-2025 | 2025 called Kickstarter's "biggest year". No video-game totals given. | A |
| K21 | Kickstarter Discover JSON (`/discover/advanced?format=json&category_id=35`), searches for multiplayer, asymmetric, shark, underwater, ocean, fish, social deduction, party, hide and seek, proximity chat, Friday the 13th, friends, creature, online pvp, 4 player online | Funding, backers and goals of ~70 video-game campaigns, used in 01 §3. **Primary data.** | A |
| K22 | `window.current_project` JSON on the Friday the 13th, Carnival Hunt, Hide Or Die, Ahoy and Throne of Lies project pages | Full reward tiers with backer counts and promised delivery dates (01 §3.3). | A |

| K23 | kickstarter.com/help/fees (Netherlands) | 5% + 3% + €0.30 per pledge; under €10: 5% + €0.08. | A |
| K24 | help…/16236608, 16236616, 16236732 (Pledge Manager tax) | Kickstarter collects and remits VAT where it is the "deemed supplier" (written around shipped goods). Otherwise there's an optional service that collects creator-responsible tax and pays it to the creator to remit. Creators can opt out. | A |
| T1 | moore-drv.nl "Crowdfunding: let op de btw"; taxlive.nl (Grant Thornton VAT commission view); actuelerentestanden.nl | Reward crowdfunding with a direct counter-performance is a VAT-taxable supply, with VAT due on receipt. Funds are taxable profit for entrepreneurs. **Secondary sources: verify on belastingdienst.nl.** | B/C |

## Steam / Valve (read 2026-10-17)

| # | Source | Finding | Rel. |
|---|---|---|---|
| S1 | partner.steamgames.com/doc/gettingstarted/appfee | Steam Direct: **$100 per app**, recoupable once the app reaches $1,000 adjusted gross revenue. | A |
| S2 | partner.steamgames.com/doc/marketing/upcoming_events | Next Fest: **Feb 22 – Mar 1 2027**, **Jun 14–21 2027**. Spring Sale Mar 18–25 2027. Summer Sale Jun 24 – Jul 8 2027. | A |
| S3 | partner.steamgames.com/doc/features/keys | Keys may fulfil crowdfunding rewards **only for products with confirmed Steam distribution**. Beta (Release State Override) keys generally capped at **2,500 total**, and no more may be promised. Crowdfunding must be time-limited, **max 30 days**. Keys aren't for ongoing, slacker-backer or late-pledge campaigns before the game is on sale. Playtest keys may be given to backers, but Playtest access must never be sold. Parity: don't give Steam customers a worse deal than key buyers. | A |
| S4 | Steam store API `appdetails` + `appreviews` | Price, release date, developer, review totals and positive share for the comparables (01 §2). | A |
| S5 | steamcharts.com/app/{id} | Current, 24 h and all-time peak concurrent players, last-month average. Concurrent players are **not** sales. | B |

## Market / industry

| # | Source | Finding | Rel. |
|---|---|---|---|
| M1 | GameDiscoverCo newsletter, S. Carless, 2026-02-10, "What worked for video game crowdfunding in 2025?" (data: ICO Partners, T. Bidaux) | 443 successful video-game Kickstarters in 2025, ~$26M pledged, about $58k average per successful campaign. The top 11 raised over $500k. Autonomica launched with >10k followers. Community building is the cornerstone; ads only complement it. No failure rate available. | B |
| M2 | en.wikipedia.org/wiki/Friendslop (read 2026-10-17) | Friend-group co-op indie games: low entry barrier, proximity chat common, usually ≤$20. Examples: Among Us, Lethal Company, Content Warning, PEAK, R.E.P.O. The term spread after PEAK (2025). | B |
| M3 | en.wikipedia.org/wiki/Among_Us | 2018 launch averaged 30–50 concurrent players. Popularised by streamers in July 2020. 3.8M concurrent peak in Sept 2020. | B |
| M4 | en.wikipedia.org/wiki/Depth_(video_game) + depthgame.com/press (via search) | Depth: asymmetric divers-versus-sharks, PC Nov 2014, started as a 2009 student mod. Its developer said playing the shark was "confusing, and sometimes outright boring" early on. IP later sold to Ammobox Studios. | B |
| M5 | Friday the 13th: The Game coverage (checkpointgaming.net 2020, primagames, soapcentral) | Dedicated servers ended Nov 2020. Delisted 2023 amid licensing issues. Online services ended by Jan 1 2025. | B/C |
| M6 | dualshockers.com (Hide Or Die) | Passed its $54.5k goal two weeks early. Later reached Early Access. Not found in Steam store search on 2026-10-17. | B/C |
| M7 | comixtribe.substack.com (quoting a Kickstarter outreach lead), diesoft.games recap, rpgdrop.com Botany case study, pledgebox.com | Follower→backer conversion: about 31% average (comics, quoted), 8–11% planning rate (Botany), backers 0.5×–2× pre-launch signups (anecdote). **The sources disagree widely, and none is video-game data.** | C |
| M8 | Review-to-sales multiplier (steampageanalyzer.com, wayline.io calculators; origin: S. Carless / J. Birkett) | 20–60 sales per Steam review, ~30× typical. Error ±30–50%. **Used only as rough ranges.** | C |
| M9 | Stripe Atlas pricing (rho.co review, unilink.us guide) | $500 one-time (includes year-1 registered agent). Agent $100/yr after that. Delaware franchise tax ~$300/yr for an LLC, and C-corps differ. **Verify at stripe.com/atlas.** | C |
| M10 | help.gamefound.com/article/210 | Gamefound accepts companies or individuals "registered in accepted countries" (no list). Board games, TTRPGs, peripherals and gaming media are listed; **video games aren't listed**. | A (but incomplete) |

## Successful campaign budget and spending cases (read 2026-10-18)

| # | Source | Finding used in `research/CAMPAIGN_BUDGET_CASES.md` | Rel. |
|---|---|---|---|
| B1 | [Kickstarter: Friday the 13th: The Game](https://www.kickstarter.com/projects/randygbk/friday-the-13th-the-game) | $700,000 goal; $823,704 pledged; 12,218 backers; campaign dates. | A |
| B2 | [PlayStation Universe interview with Friday the 13th co-creator](https://www.psu.com/news/exclusive-interview-summer-camp-renamed-friday-the-13th-the-game-kickstarter-begins/) | Team had cash for original *Summer Camp* concept; Kickstarter was sought to fund the expanded licensed vision. | B (direct interview quote) |
| B3 | [Kicktraq: Friday the 13th](https://www.kicktraq.com/projects/613356213/friday-the-13th-the-game/) | Captured result and average pledge, used as supplemental campaign-history data only. | B |
| B4 | [Kickstarter: Carnival Hunt](https://www.kickstarter.com/projects/badraa/carnival-hunt) | $50,000 goal; $122,426 pledged; 1,470 backers; campaign dates. | A |
| B5 | [Kicktraq: Carnival Hunt](https://www.kicktraq.com/projects/badraa/carnival-hunt/) | Result, average pledge, and campaign updates including console-port stretch goal. | B |
| B6 | [Kickstarter: Day of Dragons campaign update](https://www.kickstarter.com/projects/beawesomegames/day-of-dragons/posts/2619918) | Official Kickstarter result: $533,938 pledged by 12,123 backers; creator update references initial scope. | A |
| B7 | [Steam: Day of Dragons developer announcement](https://store.steampowered.com/news/posts/?appids=1088090&enddate=1626359783&feed=steam_community_announcements) | Creator says original $12,000 aim was for three models; after the campaign, funds enabled 20 months of development and a ground-up custom game including models, audio, animation, mechanics, effects, music, UI, and code. | A (first-party developer statement) |
| B8 | [Kicktraq: Day of Dragons](https://www.kicktraq.com/projects/beawesomegames/day-of-dragons/) | Supplemental result capture differs from Kickstarter by $48 and two backers; illustrates why Kickstarter is the final-total authority. | B |
| B9 | [Kickstarter: Moon Hunters](https://www.kickstarter.com/projects/kitfoxgames/moon-hunters-a-myth-weaving-rpg) | C$45,000 goal; C$178,986 pledged; 6,044 backers. | A |
| B10 | [Moon Hunters Kickstarter postmortem by Kitfox co-founder](https://www.gamedeveloper.com/business/moon-hunters-kickstarter-post-mortem-stats-anxieties-and-success) | Goal planned to cover campaign labor and roughly two more development months plus taxes, fees, and rewards; other cash sources supported production; no complete actual-spend ledger. | A (creator-authored) |
| B11 | [Kickstarter: The Flame in the Flood](https://www.kickstarter.com/projects/molassesflood/the-flame-in-the-flood) | $150,000 goal; $251,647 pledged; 7,420 backers. | A |
| B12 | [Forrest Dowling interview on The Flame in the Flood budget](https://cliqist.com/2014/10/19/flame-flood-interview/) | Creator's approximate planned allocation for the initial $150,000 goal and caveat that the percentages varied with reward mix and overfunding. | B (direct creator quote) |
| B13 | [Kickstarter: LOST EMBER](https://www.kickstarter.com/projects/mooneyestudios/lost-ember) | €100,000 goal; €326,103 pledged; 7,734 backers. | A |
| B14 | [Mooneye Studios: LOST EMBER budget postmortem](https://www.mooneyestudios.com/news/games/lostember/lessons-learned-lost-ember-post-mortem-part-four/) | Creator-reported ~€800,000 development budget; ~€220,000 Kickstarter contribution after taxes and reward production; other funding sources; ~€105,000 release marketing breakdown; excluded costs and later funding needs. | A (first-party studio postmortem; unaudited) |
