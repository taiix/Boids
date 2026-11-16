# Conversion copy and visual practice for a Kickstarter video-game page (Reef Run)

Researcher note on evidence quality (read first):
- "FETCHED" = I opened the page and read the content. "SNIPPET" = I only saw the search-engine summary; the page was not opened (403, socket error, or call budget). Treat SNIPPET items as unverified leads.
- Several of the suggested sources could not be opened: Kickstarter Creator Handbook (403), LaunchBoom page-conversion article (403), BackerKit campaign-page-design article (403), ICO Partners "Crowdfunding for Video Games: Essentials" (403), the r/IndieDev "3 funded games" post (socket closed twice), GamesRadar PEAK interview (body truncated). Nothing from Full Charge (Sam Jarvis), Crowdfunding Nerds, Jellop, Chris Zukowski on Kickstarter specifically, or GDC talks was retrieved.
- Overall: measured evidence on page layout for video-game Kickstarters is thin. Most layout advice in circulation is vendor opinion (PledgeBox, Wayline, StraySpark, BackerKit, LaunchBoom sell services or are SEO content) with no published methodology. The hard numbers that exist are about campaign totals, wishlist gains and video completion, not about page layout A/B tests.

## 1. Mobile share of Kickstarter traffic, and sizing of images / GIFs / text for phones

### Takeaway
I found no published 2025-2026 figure for the mobile share of Kickstarter traffic; treat any percentage as unverified. The only hard specs are Kickstarter's own: story images at 700 px wide or more, 50 MB max per image (GIF included), and the project video is displayed from a 640x360 transcode, so everything must read at small sizes.

### Cited Findings
- Kickstarter help center (FETCHED, current): "Images must be 50MB or less"; recommended formats JPEG, PNG, GIF; TIFF/BMP/HEIC/HEIF not accepted; scale images to 700 px wide or higher before upload because uploads are compressed and look lower-resolution, especially on high-DPI screens. — [Kickstarter Help](https://help.kickstarter.com/en-us/articles/16236740-how-do-i-include-images-or-other-media-in-my-project-description-and-updates)
- Same article: no separate GIF limit beyond 50 MB; some animated GIFs show as static in the editor, in which case Kickstarter recommends replacing them with WEBP under 50 MB. — [Kickstarter Help](https://help.kickstarter.com/en-us/articles/16236740-how-do-i-include-images-or-other-media-in-my-project-description-and-updates)
- Same article: project video max 5 GB; Kickstarter generates a 640x360 (16:9) version for the page; suggested starting bitrate 1500 kbps. Videos inside the story can only be embedded by URL (YouTube, Vimeo, etc.). Alt text can be added per story image while the project is draft or live. Captions under images are not mentioned. — [Kickstarter Help](https://help.kickstarter.com/en-us/articles/16236740-how-do-i-include-images-or-other-media-in-my-project-description-and-updates)
- Older creator tip (2017, SNIPPET, dated): resize GIFs to the story column width (then 680 px) and keep each around 5-10 MB so the page loads quickly. — [Engineerable blog, 2017](https://engineerable.blogspot.com/2017/10/kickstarter-tip-using-animated-gifs.html)
- Kickstarter serves images and GIFs through imgix with responsive delivery (srcset / Client Hints), i.e. the platform down-scales for phones; the creator does not control the mobile rendition. (SNIPPET, vendor case study) — [imgix customer story](https://imgIX.com/customers/kickstarter)
- Vendor advice (SNIPPET, no data): design the page mobile-first; many backers view on phones, so GIFs and videos should be optimised for small screens. — [LaunchBoom](https://launchboom.com/blog/5-tips-to-design-a-landing-page-for-crowdfunding-that-converts); [Wayline](https://www.wayline.io/blog/kickstarter-crash-course-tutorial-ux)
- BackerKit (SNIPPET): keep copy short and scannable with subheads, bullets, images, infographics and animated GIFs. — [BackerKit guide](https://www.backerkit.com/blog/guides/the-practical-guide-to-planning-a-crowdfunding-campaign/create-a-compelling-campaign-page/)
- Adjacent evidence, not Kickstarter (SNIPPET): NextAfter's donation-page tests found copy matters more on mobile than desktop because the small viewport concentrates attention on the message. — [NextAfter](https://www.nextafter.com/blog/how-responsive-is-your-mobile-donation-page/)

### Inferences
- A 700 px-wide story image shown on a ~360-390 px-wide phone is scaled to roughly half size. Text baked into graphics therefore needs to be about twice the size you want it to appear: for ~14-16 px apparent size on a phone, body text in a 700 px graphic should be roughly 28-32 px and header text 48 px or more. This is arithmetic from the Kickstarter spec, not a sourced rule. If you author at 1400 px for sharpness, double those numbers.
- Because the GIF limit is generous (50 MB) but mobile data and load time are not, the old 5-10 MB-per-GIF habit is still a sensible ceiling; total page weight with 6-8 GIFs matters more than any single file. WEBP is explicitly sanctioned by Kickstarter and will be far smaller than GIF at the same quality.
- Put critical statements in real text, not only inside images: real text reflows on phones, is searchable, and survives a GIF failing to animate.

### Gaps
- No verified mobile-share percentage for Kickstarter (overall or Games category) for 2025-2026. Figures like "over half of traffic is mobile" circulate in vendor blogs; I could not trace one to Kickstarter data. The creator's own dashboard (and Google Analytics, if attached) is the only reliable source once a pre-launch page is live.
- No sourced minimum font size for text in graphics, no sourced recommended GIF duration, and no measured data on GIF count versus conversion.
- Current exact story-column display width on desktop (680 vs 700 px) was not confirmed; Kickstarter only states "700px or higher".

## 2. First screen of the story, story length, headers, number of GIFs, captions, WIP labelling

### Takeaway
There is no measured evidence on story layout for games; the consistent expert and backer opinion is "video at the top that gives an overview, then gameplay shown immediately, scannable sections, one clear pitch line, clear statement of what the money is for." Backers split into video-watchers and image-scanners, so the story must work with the video unwatched.

### Cited Findings
- Kickstarter's own superbacker interviews (FETCHED, Sept 24 2024, tabletop-leaning backers): one backer wants "a video at the top that gives an overview"; another "rarely watches videos" and scans images and inline content instead, finding it more substantive. Red flags named: no video, poorly written copy, low participation, high prices. Green flags: nearly complete offering and downloadable quick-start material (the tabletop equivalent of a demo). — [Kickstarter Updates](https://updates.kickstarter.com/10-crowdfunding-tips-from-superbackers/)
- Near-Mage postmortem (FETCHED, 2021, older): the team built all copy around one elevator pitch ("Study Magick in Transylvania!") after their earlier game's messaging tried to show too many elements at once and came across unclear; they regard visuals as the most important marketing asset, above copy. — [Game Developer](https://www.gamedeveloper.com/marketing/kickstarting-a-game-in-2021-lessons-learned-from-near-mage-s-successful-campaign)
- Wandering Village postmortem (FETCHED, 2021, older): "Put lots of effort into the design of the KS page and especially into the trailer." No layout specifics given. — [Game Developer](https://gamedeveloper.com/business/the-wandering-village---kickstarter-success-without-a-playable-demo)
- ModDB tips (SNIPPET, undated): people scroll through the page quickly, so structure matters. — [ModDB](https://www.moddb.com/news/tips-for-kickstarter)
- PledgeBox (SNIPPET, vendor): decide whether the campaign funds core production, validates an audience, or reaches a milestone; blurring those makes the page muddy. — [PledgeBox](https://www.pledgebox.com/post/crowdfunding-video-games)
- Wayline (SNIPPET, vendor/own-campaign anecdote, July 2025): a page that reads like a manual creates doubt; their own campaign suffered from information overload and unclear goals; recommends short videos/GIFs of the core gameplay loop. — [Wayline](https://www.wayline.io/blog/kickstarter-crash-course-tutorial-ux)
- Older indie roundup (SNIPPET, 2016): do not make the page "an extended back of the box." — [Game Developer](https://gamedeveloper.com/business/11-video-game-kickstarter-tips-from-successfully-funded-indies)

### Inferences
- Suggested first screen for Reef Run (opinion built from the above): project video (gameplay within seconds) -> one-line hook as real text (the "one of you is the shark, and it can hear you" premise) -> one looping GIF that shows the premise with no reading required -> platform/price/player-count line -> then the story. This serves both the video-watcher and the scroller.
- Section headers as images are fine for branding, but pair each with a line of real text directly underneath, since image text shrinks ~50% on phones (see section 1) and alt text is the only fallback.
- One GIF per mechanic or claim, each with a one-sentence caption in real text saying what the viewer is seeing, is the pattern that fits "scannable" advice. I found no source giving an optimal number.
- An "in-engine footage, work in progress" label is consistent with the trust signals backers name (transparency, nearly complete offering) and costs nothing. I found no source that measures its effect.

### Gaps
- No sourced recommendation on story word count for video-game campaigns, on image versus text headers, on GIF count, or on caption practice. The Kickstarter Creator Handbook, LaunchBoom and BackerKit page-design articles likely cover some of this but returned 403.
- No source found on WIP / in-engine labelling conventions or their effect on trust.

## 3. Project image and title/subtitle

### Takeaway
Hard limits are confirmed (60-character title, 135-character subtitle, image at least 1024x576 cropped to 16:9); what "performs" in discovery is opinion only. The practical test cited is legibility at thumbnail size.

### Cited Findings
- Kickstarter's updates blog states the limits: up to 60 characters for the title and 135 for the subtitle. (SNIPPET of official Kickstarter posts) — [Kickstarter Updates: 10 Ways To Level Up Your Campaign Page](https://updates.kickstarter.com/level-up-your-campaign-in-2024/); [Kickstarter Updates: publishing project page](https://updates.kickstarter.com/how-to-create-a-great-kickstarter-publishing-project-page)
- Project image: "at least 1024x576 pixels", cropped to 16:9 (SNIPPET; quoted by third parties from Kickstarter's editor instructions; the official help article was not opened). — [Kickstarter help article (not opened)](https://help.kickstarter.com/hc/en-us/articles/115005139153); [BackerKit: how to create images](https://help.backerkit.com/article/729-how-to-create-images-for-your-campaign)
- Older design advice (2014, SNIPPET, comics-focused): check the project image at about 200x150, the size most people see while browsing. — [ComixTribe](https://comixtribe.com/?p=3216)
- Superbackers are loyal to a category and browse inside it, so the image/title must signal the category and audience clearly. (FETCHED) — [Kickstarter Updates](https://updates.kickstarter.com/10-crowdfunding-tips-from-superbackers/)
- Wandering Village: about 45% of backers found the campaign through Kickstarter itself, so the discovery thumbnail and title carried nearly half the backer count. (FETCHED, 2020 campaign) — [Game Developer](https://gamedeveloper.com/business/the-wandering-village---kickstarter-success-without-a-playable-demo)
- A Game Developer author reported about 40% of funding from Kickstarter itself and notes many projects see 30%, 20% or less. (SNIPPET, 2021) — [Game Developer](https://gamedeveloper.com/business/mobile-game-success-on-kickstarter-9-tips-for-2021)

### Inferences
- With no audience, Kickstarter-internal discovery could plausibly be a large share of Reef Run's backers, so the project image should be judged at thumbnail size on a phone: logo legible, one clear subject (shark versus fish), no small text.
- Title formula that fits 60 characters and the category-browsing behaviour: "Name - short genre/hook" (for example game name plus "4-player hide-and-seek party game"); use the 135-character subtitle for the unique mechanic (the shark hears your voice chat) and platform (PC/Steam). This is my inference; no source measured title formulas.

### Gaps
- No evidence on key art versus gameplay screenshot versus logo-on-art for the project image, nor on text-on-image, for video games in 2025-2026.
- No data on how Kickstarter search weights title versus subtitle.

## 4. Trailer / campaign video

### Takeaway
Guidance converges on a short, gameplay-first video: gameplay inside the first 10 seconds and total length around 90 seconds to 2.5 minutes. One real completion datapoint exists (20% watched to the end). I found no evidence that omitting a face-to-camera segment hurts video-game campaigns.

### Cited Findings
- Wandering Village (FETCHED): 40k people played the project video and 20% watched it to the end. — [Game Developer](https://gamedeveloper.com/business/the-wandering-village---kickstarter-success-without-a-playable-demo)
- StraySpark playbook (FETCHED, Apr 23 2026, unsourced author opinion): "90 seconds maximum, gameplay-heavy, hook in the first 10 seconds"; bad trailers recur in failed-campaign postmortems; pay a professional editor. — [StraySpark](https://www.strayspark.studio/blog/kickstarter-indie-game-campaign-playbook-2026)
- A game developer reports showing gameplay within the first 10 seconds and seeing better pickup; says Kickstarter suggested 2-3 minutes. (SNIPPET, older) — [Game Developer: Kickstarter Guide for Indie Game Developers](https://www.gamedeveloper.com/business/kickstarter-guide-for-indie-game-developers)
- Survey of ~100 campaigns, all categories (SNIPPET, older, vendor): typical length around 2:00-2:30; success rate falls off for videos over 4 minutes; videos that reveal the product in the first 10-20 seconds did best. — [Videopixie benchmark (mirror)](https://hn.jakechampion.name/reader/https://www.videopixie.com/blog/crowdfunding-video-cost-benchmark-what-works-how-much-to-spend)
- Superbackers (FETCHED): no video is a red flag for one backer; another rarely watches videos at all. — [Kickstarter Updates](https://updates.kickstarter.com/10-crowdfunding-tips-from-superbackers/)
- Kickstarter transcodes the project video to 640x360 for the page. (FETCHED) — [Kickstarter Help](https://help.kickstarter.com/en-us/articles/16236740-how-do-i-include-images-or-other-media-in-my-project-description-and-updates)

### Inferences
- If only about one in five viewers finishes, the pitch, the premise and the ask must all land in the first 20-30 seconds; anything after 60 seconds is for the already-interested.
- At 640x360, small HUD text and subtitles are unreadable: use large on-screen captions and make sure the voice-chat premise is understandable with sound off (burned-in subtitles of what players say).
- For a two-person unknown team, a short team segment at the end (after gameplay) addresses the "creator credibility" factor backers describe, without delaying the hook. No source shows it is required.

### Gaps
- No 2025-2026 measured data comparing video-game campaigns with and without face-to-camera segments. The claim that faceless pages do worse is unverified for games.
- No Kickstarter-published play-through benchmarks found.

## 5. Steam cross-promotion: wishlist badge, wishlists gained, demo / Next Fest

### Takeaway
Nobody has published a measurement of whether a "Wishlist on Steam" link on the page reduces pledges; opinion is split between "it gives people an easier way out" and "it catches people who would not pledge anyway." What is well evidenced is that a Kickstarter drives substantial wishlists and that a playable demo is one of the strongest levers.

### Cited Findings
- Trash Goblin (FETCHED, GameDiscoverCo, Feb 5 2024): GBP 95k from 2,200+ backers; the campaign plus its demo "almost tripled" Steam wishlists during the Kickstarter; paid Facebook ads lost money; one cross-promo with another campaign brought nearly 6% of funds; only ~21% of the total after 10 days, strong last 48 hours. Launching with a demo is described as a strong approach. — [GameDiscoverCo](https://newsletter.gamediscover.co/p/making-a-successful-video-game-kickstarter)
- Wandering Village (FETCHED, 2020 campaign): Steam page launched the same day as the Kickstarter; ~8k wishlists in week one, ~30k over 30 days (about half from a Steam sale event); Steam wishlists were made a "social goal" with a reward; EUR 155k from 6,000+ backers. — [Game Developer](https://gamedeveloper.com/business/the-wandering-village---kickstarter-success-without-a-playable-demo)
- Near-Mage (FETCHED, 2021): deliberately synchronised Steam page launch with the Kickstarter so each sends traffic to the other; describes Kickstarter page -> Steam page -> wishlist as a funnel. No wishlist numbers. — [Game Developer](https://www.gamedeveloper.com/marketing/kickstarting-a-game-in-2021-lessons-learned-from-near-mage-s-successful-campaign)
- Counter-view (SNIPPET, attribution uncertain; surfaced alongside the r/IndieDev "3 funded games" post and an itch.io post, neither opened): asking followers to both back and wishlist gives too much choice and they take the easiest option, the wishlist. — [r/IndieDev post (not opened)](https://lr.eu.psf.lt/r/IndieDev/comments/1jou1tl/things_i_learned_from_running_3_funded_games_on); [itch.io post (not opened)](https://itch.io/post/983219)
- Nimbatus (SNIPPET, older): some visitors who will not back will wishlist instead; the free demo is credited with getting the game funded. — [Game Developer](https://www.gamedeveloper.com/business/nimbatus-how-a-free-demo-got-our-game-funded)
- Zero Sievert case (SNIPPET, 2023): a live Steam page before the Kickstarter mattered; wishlisters may back and vice versa. — [How To Market A Game](https://howtomarketagame.com/2023/01/18/how-a-pixel-art-shooter-achieved-a-million-dollar-launch-the-zero-sievert-story/)
- GameDiscoverCo 2025 review (FETCHED, Feb 10 2026, data from Thomas Bidaux / ICO Partners): 443 funded game campaigns and ~$26m in 2025, average ~$58k; a demo is "a big boon" (Prelude Dark Pain, $157k on a $47k goal, attention driven by creators and press with demo access); Kickstarter "Coming Soon" follower pages are increasingly standard (Autonomica: 10,000+ followers at launch, 12,000+ backers); paid ads only complement community building, "the cornerstone of crowdfunding". — [GameDiscoverCo](https://newsletter.gamediscover.co/p/what-worked-for-video-game-crowdfunding)
- StraySpark (FETCHED, 2026, unsourced opinion): have the Steam page live and collecting wishlists before the campaign; Next Fest just before launch is common for ambitious campaigns; playable-build campaigns outperform trailer-only "by a substantial margin" (no figures). Also lists "multiplayer or live-service dependence" as a risk factor backers are wary of. — [StraySpark](https://www.strayspark.studio/blog/kickstarter-indie-game-campaign-playbook-2026)
- SNIPPET-only numbers, exact source page not confirmed: a Chibig campaign overlapping Steam Next Fest saw ~25,000 demo players and 20,289 new wishlists; another campaign went from 10,000 to 37,000 wishlists over its Kickstarter. Candidate sources: [GameDiscoverCo $1.4m article](https://newsletter.gamediscover.co/p/how-did-this-video-game-kickstarter); [The Game Business](https://www.thegamebusiness.com/p/why-arent-more-game-developers-using).
- Kickstarter lets creators generate referral links to see which sources produce pledges; Wandering Village advises tracking every channel this way. (FETCHED) — [Game Developer](https://gamedeveloper.com/business/the-wandering-village---kickstarter-success-without-a-playable-demo)

### Inferences
- For Reef Run (no audience, Steam link not yet live): the page's single primary action should be the pledge. Place the "Wishlist on Steam" badge low on the page (after rewards explanation / near the end or in a "can't back right now?" block) and word it as a fallback or an addition, for example "Can't back today? Wishlist on Steam - it helps just as much with visibility." This reconciles both camps: it does not compete above the fold, and it catches non-backers. This is my synthesis, not a tested result.
- A dead badge (image with no link) is a trust and usability cost on a page for an unknown team. Until the Steam page is live, either omit the badge or label it "Steam page coming [month]" so it does not read as a broken link.
- The cheapest repeatable pattern in the evidence is the wishlist "social goal" (Wandering Village): it gives backers a reason to wishlist after pledging, so the two asks are sequenced instead of competing.
- A demo is the best-supported conversion and trust lever in every 2024-2026 source read. For a 4-player online game, a demo also raises the "can I get three friends" problem; a page section that explains how to find a group (Discord link) matters more than for a single-player demo.

### Gaps
- No measured A/B or before/after evidence on wishlist-badge placement or wording on a Kickstarter page.
- Chris Zukowski / howtomarketagame guidance specific to combining Kickstarter with Steam (2025-2026) was not retrieved.
- No verified 2025-2026 data on running Next Fest during versus before a campaign; the Chibig figure is unverified.
- Wishlist-to-backer and backer-to-wishlist conversion rates: none found.

## 6. Party / multiplayer persuasion ("friendslop" lessons)

### Takeaway
The 2024-2026 co-op hits spread through short clips of friends reacting (proximity-chat panic, laughter), streamer play, and a low price that makes a group purchase easy; none of this evidence comes from crowdfunding, so applying it to a Kickstarter page is inference.

### Cited Findings
- AppMagic analysis (FETCHED, Mar 6 2026; vendor estimates, not independently verified): these games are built around "moments worth sharing" that work as TikTok clips or stream highlights; Phasmophobia (2020) proved co-op with proximity voice chat is watchable and clippable. — [AppMagic](https://appmagic.rocks/blog/friendslop-steam-games-2025)
- Same source, pricing: most sit at $5-$20; Lethal Company $9.99, R.E.P.O. $9.99, Content Warning / PEAK / RV There Yet? $7.99; because they are group games, one purchase often becomes four, i.e. $20-$80 per friend group, and a low price helps friends agree to buy. — [AppMagic](https://appmagic.rocks/blog/friendslop-steam-games-2025)
- Same source, scale and caveat: 2025 Steam copies estimated at R.E.P.O. 18.5M, PEAK 15.4M; but 30-day retention is around 3% (Content Warning 19.8% D1 -> 3% D30), i.e. the genre sells on the promise of a few great sessions, not longevity. — [AppMagic](https://appmagic.rocks/blog/friendslop-steam-games-2025)
- Acer blog (SNIPPET): the genre thrives on funny clips and proximity-chat disasters; friends panicking, shouting, falling makes ideal short-form content. — [Acer Corner](https://blog.acer.com/en/discussion/3477/what-are-friendslop-games)
- Streams Charts (SNIPPET): R.E.P.O. was among the top games by peak streamer activity in March 2025. — [Streams Charts](https://streamscharts.com/news/repo-twitch-viewership)
- AV Club (SNIPPET): streamers took to Lethal Company for emergent stories "ranging from terrifying to hilarious". — [AV Club](https://www.avclub.com/not-every-co-op-game-is-friendslop)
- PEAK's studio says it studied games like R.E.P.O. to find what makes them fun and embraces the label (headline only; body not retrieved). — [GamesRadar](https://www.gamesradar.com/games/co-op/we-proudly-wear-the-friendslop-badge-peak-studio-unpacks-one-of-the-biggest-trends-on-steam-and-beyond-after-studying-games-like-repo-to-find-out-what-makes-them-fun)
- Risk signal for crowdfunding specifically: StraySpark lists multiplayer dependence among things that make backers hesitate (unsourced opinion). — [StraySpark](https://www.strayspark.studio/blog/kickstarter-indie-game-campaign-playbook-2026)

### Inferences
- The persuasive unit for this genre is a clip with audio of real people reacting, not a feature list. On a Kickstarter page GIFs are silent, so: (a) the project video should carry real playtest voice audio (laughter, whispering, someone getting caught because they talked), with burned-in subtitles; (b) story GIFs should have captions that quote what was said ("'Don't laugh, it can hear us'") so the voice mechanic reads without sound; (c) embed one or two short YouTube clips by URL for people who want sound.
- "One of you is the shark" second-person framing puts the reader's own friend group in the scene, which is what the clip culture does implicitly. Lead with roles ("3 of you hide. 1 of you hunts. The shark hears every word.") before features.
- Address the multiplayer worry head-on: state player count, whether it works with fewer than 4, online only or not, matchmaking or friends-only, and how backers find groups (Discord). Tier design can borrow the genre's group-purchase logic (a 4-key "pod" tier), mirroring the "one purchase becomes four" dynamic.
- Low price expectations set by the genre ($8-$10 on Steam) should be visible on the page so the base tier (EUR 12) reads as fair; say what the Steam price will be.
- Streamer reactions are the strongest proof in this genre, but Reef Run has none yet; do not fake them. Honest substitutes are in section 7.

### Gaps
- No evidence found on party/co-op games' performance on Kickstarter specifically, nor any crowdfunding postmortem from a Lethal Company-style game. All genre evidence is from Steam launches that did not crowdfund.
- No developer-attributed quotes retrieved (PEAK, R.E.P.O., Content Warning teams) on which marketing assets worked; and no retrieved evidence on "one of you is the X" framing or Among Us-style marketing.

## 7. Honest social proof and trust elements before an audience exists

### Takeaway
Backers say they judge credibility by track record, page quality, transparency, a nearly finished product and something they can try; for a first-time team the substitutes that sources support are a playable demo, evidence the game already exists, clear statement of what the money covers, and visible community numbers.

### Cited Findings
- Superbackers (FETCHED): credibility signals are number of past campaigns, how well the page is written, transparent updates; green flags are "a nearly complete offering" and downloadable previews; one backer of ~200 projects notes only one failed to deliver, i.e. delivery risk is what they screen for. — [Kickstarter Updates](https://updates.kickstarter.com/10-crowdfunding-tips-from-superbackers/)
- Near-Mage (FETCHED): a stated lesson was failing to make clear that the goal did not cover full production costs; they also cite build count ("build 68") as part of their story. — [Game Developer](https://www.gamedeveloper.com/marketing/kickstarting-a-game-in-2021-lessons-learned-from-near-mage-s-successful-campaign)
- GameDiscoverCo 2025 review (FETCHED): campaigns later in development are now normal and more solid plans help, because fans are wary of projects far from launch drifting from promises. — [GameDiscoverCo](https://newsletter.gamediscover.co/p/what-worked-for-video-game-crowdfunding)
- Cinevva guide (FETCHED, Jan 2026, mostly unsourced): "showing beats promising"; have a demo or vertical slice; Alzara (5,000+ backers, studio closed without delivering) is the current cautionary tale that makes backers sceptical. — [Cinevva](https://app.cinevva.com/guides/kickstarter-for-indie-games)
- Alzara's failure was widely discussed on r/gamedev in 2025 (SNIPPET). — [r/gamedev mirror](https://redlib.hackliberty.org/r/gamedev/comments/1le6wfs/one_of_the_most_backed_video_games_on_kickstarter)
- Kickstarter "Coming Soon" follower counts function as visible pre-launch proof (Autonomica 10,000+ followers). — [GameDiscoverCo](https://newsletter.gamediscover.co/p/what-worked-for-video-game-crowdfunding)

### Inferences
- Honest proof Reef Run can show today: a "what exists today / what the money adds" two-column table; a dated build timeline with real screenshots; all GIFs labelled as real in-engine footage; named, attributed playtest quotes (first name, context, with permission) and ideally a clip of the playtest; the team's faces and location; a plain budget breakdown of EUR 14,000 and a statement of what happens if only the goal is reached; Discord member count only once it is not embarrassing.
- Post-Alzara, a small goal with an already playable game is itself a selling point: say explicitly that the game is playable now and the campaign funds finishing, not starting.

### Gaps
- No measured data on the effect of playtest quotes, "what exists today" tables or build-history sections. These are inferences from stated backer preferences.
- Success-rate figures conflict: Cinevva cites ~32% lifetime success for video games (unnamed 17,000-campaign dataset), StraySpark cites an 83% figure attributed to Kickstarter's 2024 year-end update (likely all Games including tabletop). Neither verified at source; do not quote either on the page.

## 8. Common page mistakes called out by experts and Kickstarter

### Takeaway
The repeated mistakes are: launching cold with no audience or demo, a weak or slow trailer, muddled messaging, unclear use of funds, too many or scope-expanding stretch goals, and over-promising delivery.

### Cited Findings
- No video; poorly written copy; high prices; low visible participation. (FETCHED) — [Kickstarter Updates](https://updates.kickstarter.com/10-crowdfunding-tips-from-superbackers/)
- Holding back content as stretch goals that should have been in the base offer. (FETCHED) — [Kickstarter Updates](https://updates.kickstarter.com/10-crowdfunding-tips-from-superbackers/)
- Trying to show too many elements at once so the message is unclear; surprising people on launch day with no build-up; not explaining that the goal does not cover full production. (FETCHED) — [Game Developer, Near-Mage](https://www.gamedeveloper.com/marketing/kickstarting-a-game-in-2021-lessons-learned-from-near-mage-s-successful-campaign)
- Bad trailer; no demo or vertical slice; cold "concept art and a trailer" pitch; more than 3-5 stretch goals or scope-expanding ones; physical rewards without shipping experience; delivery dates beyond 18-24 months; under six months of community building. (FETCHED, unsourced opinion, 2026) — [StraySpark](https://www.strayspark.studio/blog/kickstarter-indie-game-campaign-playbook-2026)
- Launching with no audience; expecting the campaign itself to generate awareness; underestimating the daily workload of a 30-day campaign; needing the money to start development. (FETCHED, unsourced opinion, 2026) — [Cinevva](https://app.cinevva.com/guides/kickstarter-for-indie-games)
- Relying on paid ads: Trash Goblin's Facebook ads returned less than they cost, whereas Wandering Village's returned ~2.3-3x; results are inconsistent and ads are a complement to community, per Thomas Bidaux. (FETCHED) — [GameDiscoverCo 2024](https://newsletter.gamediscover.co/p/making-a-successful-video-game-kickstarter); [Game Developer](https://gamedeveloper.com/business/the-wandering-village---kickstarter-success-without-a-playable-demo); [GameDiscoverCo 2026](https://newsletter.gamediscover.co/p/what-worked-for-video-game-crowdfunding)
- Page reads like a manual / information overload. (SNIPPET, vendor anecdote) — [Wayline](https://www.wayline.io/blog/kickstarter-crash-course-tutorial-ux)
- Timing notes from Wandering Village (2020 data, older): launch Tuesday, end Friday; campaigns launching after mid-October reportedly earn ~10% less; set up a pre-launch page ~4 weeks ahead; use the 48-hour reminder. (FETCHED) — [Game Developer](https://gamedeveloper.com/business/the-wandering-village---kickstarter-success-without-a-playable-demo)

### Inferences
- The biggest risk for Reef Run is not a page-layout error but the cold start: every 2024-2026 source ranks pre-launch audience and a playable build above page polish. Page work should therefore prioritise (1) the Coming Soon page live early, (2) demo or at least a public playtest, (3) trailer opening, (4) clarity of pitch and funds, and only then visual polish.
- Mid-November onward is a weak launch window by the one dated timing source; a launch after the holidays (February has the best month in Cinevva's unnamed dataset, 35% versus 28% for December) fits better. Treat both timing figures as low-confidence.

### Gaps
- No statement from Kickstarter games-category staff (2025-2026) on page mistakes was retrieved; the Creator Handbook returned 403.
- No Reddit postmortems with numbers from 2025-2026 were successfully opened.
