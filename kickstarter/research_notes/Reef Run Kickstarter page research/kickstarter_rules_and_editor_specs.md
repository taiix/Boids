# Kickstarter rules, features and project-editor specs for a video game campaign (state as of November 2026)

All sources were checked on **2026-11-16**. Access notes that apply to the whole file:

- `kickstarter.com/rules`, `kickstarter.com/rules/prohibited` and `kickstarter.com/help/fees` returned **HTTP 403** to automated fetching. Nothing in this file is quoted directly from those three pages. Rule statements that depend on them are marked **UNVERIFIED (official page blocked)** and must be read in a browser before the page is finalised.
- `help.kickstarter.com`, `updates.kickstarter.com`, `start.kickstarter.com` and `partner.steamgames.com` were readable. Facts from them are marked **OFFICIAL**.
- Fetched pages were read through a summarising tool, so wording is paraphrase unless in quotation marks. Items found only in a search-result snippet (page not opened) are marked **SNIPPET**.

## 1. Story editor: supported content blocks, clickable images, external links

### Takeaway
The story editor is a constrained rich-text editor (no HTML, no tables or columns documented). Officially confirmed: headings with automatic anchors/table of contents, bold, bulleted lists, images with caption + URL + alt text, images used as section headers, and URL-pasted embeds. An image can carry a URL, so a "Wishlist on Steam" badge image linking to the Steam page is supported by the editor; no official limit on external links was found, but the Rules page could not be read.

### Cited Findings
- OFFICIAL: The contextual toolbar changes with the selection. Clicking an image offers **captions, URLs, and alt text**; selecting text offers formatting such as bolding. (Post dated 13 Sep 2023, update note 23 Oct 2023 — older source, still the only official feature description found.) — [Kickstarter Updates: Text Editor 2.0](https://updates.kickstarter.com/text-editor-2-0/)
- OFFICIAL: Selecting a heading automatically creates a page anchor; anchored headings form a clickable table of contents. Images or custom graphics can be used as headers. — [Kickstarter Updates: Text Editor 2.0](https://updates.kickstarter.com/text-editor-2-0/)
- OFFICIAL (SNIPPET): To make an image a section header, select the image and choose "Use image as section heading" from the options above it. — [Help: How do I add a table of contents to my project's story](https://help.kickstarter.com/hc/en-us/articles/50023436957595-How-do-I-add-a-table-of-contents-to-my-project-s-story)
- OFFICIAL: Kickstarter's own page advice (25 Oct 2023) lists graphic headers, **bulleted lists**, embedded media, page anchors/table of contents, and image captions/URLs/alt text as editor elements, and says to put a brief summary at the top because mobile users see only that section at a glance. — [Kickstarter Updates: 10 Ways To Level Up Your Campaign Page](https://updates.kickstarter.com/level-up-your-campaign-in-2024/)
- OFFICIAL: Video in the story is by **URL embed only** ("In project descriptions, URL embedding is currently the only option"): paste a YouTube, Vimeo or similar URL. YouTube Shorts URLs must be changed from `shorts/` to `watch?v=`. Audio embeds: SoundCloud, Bandcamp, Spotify. Live streams (YouTube, Twitch, Facebook Live) can be embedded in the description during the funding period. — [Help: How do I include images or other media in my project description and updates](https://help.kickstarter.com/en-us/articles/16236740-how-do-i-include-images-or-other-media-in-my-project-description-and-updates)
- OFFICIAL: The "⁋" button opens the contextual toolbar for images and media; story images are replaced/deleted by selecting them (they turn green) and pressing delete. Alt text on description images cannot be changed after the campaign ends. — [Help: images or other media](https://help.kickstarter.com/en-us/articles/16236740-how-do-i-include-images-or-other-media-in-my-project-description-and-updates)
- OFFICIAL: Project title and description can still be edited while the project is live (SNIPPET). — [Help: Can a project be edited after launching](https://help.kickstarter.com/en-us/articles/16236599-can-a-project-be-edited-after-launching)
- THIRD-PARTY, undated/possibly outdated: one article claims the description editor does not support text styling, so branded looks need graphics. — [Lucidpress on Medium](https://medium.com/lucidpress/how-to-fix-your-kickstarter-campaign-2a888cfcf8a0)

### Inferences
- A "Wishlist on Steam" badge as a linked image is technically possible: upload the badge image, select it, set its URL to the Steam store page. Styled text, coloured buttons, multi-column layouts and tables must be baked into images, since none of those are documented editor features.
- Because project video in the story is embed-only, gameplay clips inside the story are either GIF/WebP images or YouTube/Vimeo embeds; self-hosted MP4 in the story is not an option.

### Gaps
- No official page found that enumerates the full toolbar (heading levels, italics, numbered lists, text links, dividers). Italic, numbered lists and text hyperlinks are very likely present but **not verified on an official page**.
- Tables, columns, raw HTML/CSS: no official statement found either way; absence from every official description suggests they are not supported. **Unverified.**
- Any rule limiting external links or calls to action to other stores (e.g. linking to Steam): none found in the help centre; the Rules page was blocked. **Unverified.**

## 2. Media specs: project image, story images, GIFs, project video, Discovery Mode vertical video, reward images

### Takeaway
Story/update images: max 50 MB, JPEG/PNG/GIF recommended, at least 700 px wide, recompressed on upload; animated GIFs may be shown static, and Kickstarter's own fix is a WebP under 50 MB. Project video: up to 5 GB, common formats, displayed as a 640x360 16:9 version. A 9:16 "Discovery Mode" vertical video (mobile-app feed, launched August 2026) is a separate upload in the Basics tab.

### Cited Findings
- OFFICIAL: Images must be **50 MB or less**. Most major formats accepted; **JPEG, PNG, GIF recommended**. **TIFF, BMP, HEIC, HEIF not accepted.** — [Help: images or other media](https://help.kickstarter.com/en-us/articles/16236740-how-do-i-include-images-or-other-media-in-my-project-description-and-updates)
- OFFICIAL: Scale images to **at least 700 px wide** before uploading. Uploaded images are compressed and will look lower-resolution than the original, especially on high-resolution screens. — [Help: images or other media](https://help.kickstarter.com/en-us/articles/16236740-how-do-i-include-images-or-other-media-in-my-project-description-and-updates)
- OFFICIAL: **Some animated GIFs may display as static images; if so, replace them with WEBP files under 50 MB.** (No size threshold for when this happens is given.) — [Help: images or other media](https://help.kickstarter.com/en-us/articles/16236740-how-do-i-include-images-or-other-media-in-my-project-description-and-updates)
- OFFICIAL: Uploaded video formats: **MOV, MPEG, AVI, MP4, 3GP, FLV, WMV**. **Project video up to 5 GB; update videos up to 3 GB.** Kickstarter creates a **640 x 360 (16:9)** playback version. Encoding advice: H.264 (Mac) or WMV (Windows), start at about 1500 kbps. — [Help: images or other media](https://help.kickstarter.com/en-us/articles/16236740-how-do-i-include-images-or-other-media-in-my-project-description-and-updates)
- OFFICIAL: The project image and project video are set, replaced or deleted on the **Basics** tab. — [Help: images or other media](https://help.kickstarter.com/en-us/articles/16236740-how-do-i-include-images-or-other-media-in-my-project-description-and-updates)
- OFFICIAL: A help article exists on whether a project video is required to launch (title only; content not opened). — [Help: Is a project video required to launch](https://help.kickstarter.com/en-us/articles/16236743-is-a-project-video-required-to-launch)
- OFFICIAL: Kickstarter gives no hard video-length limit in its page guide but says backers "tend to drop off after the first minute"; add subtitles/captions. (25 Oct 2023.) — [Kickstarter Updates: 10 Ways To Level Up Your Campaign Page](https://updates.kickstarter.com/level-up-your-campaign-in-2024/)
- OFFICIAL: Project image advice: uncluttered, readable at thumbnail size, no text overlays or small text; Kickstarter supplies "just launched"/"nearly funded" badges. (25 Oct 2023.) — [Kickstarter Updates: 10 Ways To Level Up Your Campaign Page](https://updates.kickstarter.com/level-up-your-campaign-in-2024/)
- SECONDHAND (blog quoting the editor text): "Your image should be at least 1024x576 pixels. It will be cropped to a 16:9 ratio." — [Stonemaier Games](https://stonemaiergames.com/?p=6229); an older guide lists 640x480 / 4:3, which predates the 16:9 guidance — [ComixTribe](https://comixtribe.com/?p=3216)
- OFFICIAL — **Discovery Mode** (vertical video): a vertical video feed in the iOS and Android apps, reachable from the Discover and Search pages. Creators upload a **9:16** video in **Project Build > Basics**, next to the normal video field. Formats **MOV, MPEG, AVI, MP4, 3GP, WMV, FLV; max 5120 MB**. Best practice: **60–90 seconds** (the same guide also says "aim for under a minute"), clear audio, captions, keep key visuals/text away from the edges where UI overlaps, real people talking preferred over a highly produced edit. Repurposed social-media video is allowed. The vertical video is moderated as part of project review. Described as a beta. — [Kickstarter Creator Guide: Discovery Mode (PDF)](https://start.kickstarter.com/hubfs/Mobile%20Discovery%20Mode%20-%20Creator%20Guide%20final.pdf)
- OFFICIAL: "Mobile Discovery Mode" release note dated **26 Aug 2026**. — [Kickstarter release notes](https://start.kickstarter.com/kickstarter-release-notes/vat-flexibility-in-pledge-manager)
- THIRD-PARTY, 2017 (old): resize animated GIFs to about 680 px wide and keep them roughly 5–10 MB for load speed. Not an official spec. — [Engineerable blog](https://engineerable.blogspot.com/2017/10/kickstarter-tip-using-animated-gifs.html)

### Inferences
- The story column renders around 680–700 px wide; exporting story images at 1400 px wide (2x) is a reasonable hedge against recompression, well under 50 MB.
- For gameplay loops, animated WebP is the safest format, since Kickstarter itself names it as the fix when GIFs turn static. MP4 cannot be uploaded into the story (embed-only).
- The page mixes "60–90 seconds" and "under a minute" for the vertical video; under 60 seconds satisfies both.

### Gaps
- Project image exact minimum (1024x576), 16:9 crop and its max file size: only secondhand; the editor's own on-screen hint is the authority. **Unverified on an official page.**
- Reward image and add-on image specs (ratio, size): **no source found.** Check the Rewards tab hint in the editor.
- The file-size threshold at which GIFs are rendered static: not published.
- Whether a project video is mandatory: article exists but was not read.

## 3. Title/subtitle limits; FAQ; Risks and challenges; "Use of AI" disclosure

### Takeaway
Title 60 characters, subtitle 135 characters (Kickstarter blog). The AI disclosure is two yes/no questions with a mandatory free-text explanation that is shown publicly on the project page; over-reliance on generative AI can get a project declined or suspended. The help article was marked "Updated this week" on 2026-11-16, so wording may have just changed.

### Cited Findings
- OFFICIAL: Title up to **60 characters**; subtitle up to **135 characters**. (25 Oct 2023 — older source.) — [Kickstarter Updates: 10 Ways To Level Up Your Campaign Page](https://updates.kickstarter.com/level-up-your-campaign-in-2024/). A third-party fulfilment site says 130 for the subtitle — [eFulfillment Service](https://www.efulfillmentservice.com/?p=259989) (non-official; conflict noted).
- OFFICIAL: AI disclosure questions: "Does your campaign page contain any generative AI?" and "Will your final project contain any generative AI?" A yes to either requires explaining which parts of the campaign page or rewards use generative AI and how. — [Help: AI policy article](https://help.kickstarter.com/hc/en-us/articles/16848396410267)
- OFFICIAL: Display on the page: one of the statements "I am using generative AI content on my campaign page" / "I plan to use generative AI content in my project", followed by the creator's explanation. The disclosure can be previewed before launch. — [Help: AI policy article](https://help.kickstarter.com/hc/en-us/articles/16848396410267)
- OFFICIAL: Disclosure does not guarantee approval. Projects that rely excessively on generative AI (AI output is most of the work or replaces the creator's own contribution) are not allowed; Trust & Safety may decline or, after launch, suspend. — [Help: AI policy article](https://help.kickstarter.com/hc/en-us/articles/16848396410267)
- OFFICIAL: Same article: Design and Technology projects must include a video of a real prototype, available to all backers throughout the campaign; AI-generated video, renderings or simulations cannot replace it. AI companion projects offering romantic/sexual companionship or mainly targeting children are out of scope. — [Help: AI policy article](https://help.kickstarter.com/hc/en-us/articles/16848396410267)
- OFFICIAL: The article showed "Updated this week" with no exact date when checked 2026-11-16. — [Help: AI policy article](https://help.kickstarter.com/hc/en-us/articles/16848396410267)
- OFFICIAL (2023 origin): The AI policy took effect 29 Aug 2023; the original version also asked about consent of owners of works used to train/produce AI content, and undisclosed AI use could lead to suspension and a ban on resubmitting. — [Kickstarter Updates: Introducing Our AI Policy](https://updates.kickstarter.com/introducing-our-new-ai-policy/)
- OFFICIAL: Risks section guidance: clearly outline risks and challenges, realistic timeline and budget with buffer, be transparent about how challenges will be overcome. (25 Oct 2023.) — [Kickstarter Updates: 10 Ways To Level Up Your Campaign Page](https://updates.kickstarter.com/level-up-your-campaign-in-2024/)
- RESEARCH (search-result summary only): a 2026 observational paper in Decision Support Systems is reported to find that visible AI disclosure coincides with a substantial funding disadvantage. — [search result summary; primary paper not opened](https://updates.kickstarter.com/introducing-our-new-ai-policy/)

### Inferences
- The current help article lists two questions and no longer foregrounds the 2023 "consent of training-data owners" question in the summary obtained; whether that question is still in the submission form is unclear.
- The "excessive reliance" clause and the AI-companion exclusion look like post-2023 additions, i.e. the policy tightened in 2025–2026, but no dated changelog was found.
- If no generative AI is used on the page or in the game, both answers are "no" and presumably no "Use of AI" section appears; this is an inference, not stated.

### Gaps
- Exact date and content of the most recent AI policy revision ("Updated this week").
- Whether a "no/no" answer shows anything on the page.
- FAQ field: character limits, when FAQs can be added (draft vs. live) — **no source opened.**
- Risks and challenges: whether it is mandatory and any character limit — **not verified.**
- Whether AI-assisted code (e.g. coding assistants) counts as "generative AI in the final project" for games — not addressed in the summary obtained.

## 4. Rules: prohibited items, honesty/prototype rule for games, renders vs real footage, stretch goals, raffles, discounts, promising Steam keys

### Takeaway
The official Rules and Prohibited Items pages could not be read (HTTP 403), so this section is the weakest: the prototype requirement and the ban on photorealistic renderings are confirmed only for hardware/product design (and Design & Technology), not for video games. Contests/raffles/coupons and financial returns are prohibited per official help text and secondhand quotes. Steam-key promises are governed by Valve (section 6).

### Cited Findings
- OFFICIAL: Projects must have a clear end goal and create something new; a project cannot be something already available elsewhere. Projects are reviewed before launch. — [Help: Can Kickstarter be used to fund anything](https://help.kickstarter.com/en-us/articles/16236681-can-kickstarter-be-used-to-fund-anything)
- OFFICIAL: Prohibited: projects that are illegal, heavily regulated or potentially dangerous for backers; **rewards the creator did not make**; **a return on investment or any monetary reward**. — [Help: Can Kickstarter be used to fund anything](https://help.kickstarter.com/en-us/articles/16236681-can-kickstarter-be-used-to-fund-anything)
- OFFICIAL: Rewards may be physical items or experiences, need not be physical, must fit Our Rules; add-ons are optional extras that complement the main reward (example given: a game expansion pack). — [Help: What can be offered as a reward](https://help.kickstarter.com/en-us/articles/16236477-what-can-be-offered-as-a-reward)
- OFFICIAL: **Hardware and product design** creators "are required to show working prototypes"; "We do not allow photorealistic renderings." The article does not mention games or software. — [Help: rules for hardware and product design projects](https://help.kickstarter.com/en-us/articles/16236428-what-are-the-rules-for-hardware-and-product-design-projects)
- SECONDHAND (blog repost of the Rules text): the prototype requirement and the ban on misleading imagery (photorealistic renderings, heavily edited or manipulated images/videos) apply "when a project involves manufacturing and distributing something complex, like a gadget". — [Stonemaier Games](https://stonemaiergames.com/?p=14139)
- SECONDHAND (blog quoting the guidelines, old): "No contests, raffles, coupons, gambling, or lifetime memberships." — [Stonemaier Games](https://stonemaiergames.com/?p=3239)
- THIRD-PARTY: referral programmes with incentives are allowed, but not structured as a contest or drawing. — [Kickbooster help](https://intercom.help/kickbooster/campaign-owners/faq/does-this-service-violate-kickstarters-terms-of-service)
- OLD (2014): a Kickstarter representative said actual gameplay is not required for games, though showing development progress is encouraged. — [Game Informer](https://gameinformer.com/b/news/archive/2014/06/03/kickstarter-drastically-overhauls-rules-and-automates-approval-process.aspx)
- OFFICIAL (Valve): Steam keys may be promoted as crowdfunding rewards only for products with confirmed Steam distribution; see section 6. — [Steamworks: Steam Keys](https://partner.steamgames.com/doc/features/keys)

### Inferences
- For a PC game the binding honesty constraint is the general rule that projects be honestly and clearly presented; the strict "working prototype / no photorealistic renders" rule targets physical products. Real in-engine footage is nevertheless the safe choice, and labelling any concept art or mock-up as such avoids a misleading-imagery complaint.
- A "win a ..." draw among backers, discount coupons, or a lifetime-membership style reward would fall under the contests/raffles/coupons prohibition. Early-bird tiers (a cheaper limited tier of the same reward) are a common practice and are not the same as a coupon, but no official text confirming this was read.
- Digital cosmetics and credits are acceptable reward types (rewards need not be physical and must be made by the creator).

### Gaps
- **UNVERIFIED (official page blocked):** the current full text of [Our Rules](https://www.kickstarter.com/rules) and [Prohibited Items](https://www.kickstarter.com/rules/prohibited), including any 2025–2026 edits. Read both in a browser.
- Whether Kickstarter states any rule on "discount versus retail price" claims (e.g. "30% off the Steam price"): **no source found.** Valve's parity rule (section 6) is the relevant constraint found.
- Stretch goals: **no official rule or help article located**; they appear to be a creator convention with no dedicated editor feature. Unverified.
- Any games-specific rule on rendered/target footage vs. real gameplay: none found on an official page.

## 5. Features: pre-launch page, Late Pledges, Pledge Manager, Pledge Over Time, add-ons, VAT, fees (NL/EUR), duration, review, Projects We Love; 2026 changes

### Takeaway
2026 changes that matter: VAT flexibility in the Pledge Manager (13 Apr 2026: creators may handle VAT themselves and VAT-inclusive pricing is now allowed), Mobile Discovery Mode (26 Aug 2026), and Pledge Over Time rules applying from 15 Jan 2026 (EUR minimum pledge 125). Late Pledges charge immediately, exclude SEPA and Pledge Over Time, and conflict with Valve's key rules for an unreleased game.

### Cited Findings
- OFFICIAL — **Duration:** 1 to a maximum of 60 days (SNIPPET); the deadline cannot be changed after launch (SNIPPET). — [Help: What is the maximum project duration](https://help.kickstarter.com/hc/en-us/articles/115005128434-What-is-the-maximum-project-duration)
- OFFICIAL: Kickstarter recommends about four weeks, launching early in the week and early in the day. (25 Oct 2023.) — [Kickstarter Updates: 10 Ways To Level Up Your Campaign Page](https://updates.kickstarter.com/level-up-your-campaign-in-2024/)
- OFFICIAL (Valve): crowdfunding campaigns that promise Steam keys must be time-limited, **maximum 30 days**. — [Steamworks: Steam Keys](https://partner.steamgames.com/doc/features/keys)
- OFFICIAL — **Fees:** 5% platform fee on funds collected from successful projects; Stripe processing "roughly 3-5%"; no fees if unsuccessful. In the **Pledge Manager**: 5% on all funds except taxes, and Stripe's processing fee applies to the full payment **including taxes**. Page dated 2 Sep 2026. Per-country fixed fees are on kickstarter.com/help/fees (blocked). — [Help: What are the fees](https://help.kickstarter.com/hc/en-us/articles/115005028634-What-are-the-fees)
- OFFICIAL — **Late Pledges:** collect pledges after a successful campaign; must be enabled while the project is in draft or live, not after it ends (SNIPPET). Payment is taken immediately and paid out in a new payout; creators can raise reward prices for late pledges; add-ons can be selected; SEPA not available; backers cannot modify or cancel through Kickstarter; one pledge per backer; **Pledge Over Time not available** for late pledges; late backers get backer-only updates and comments. — [Help: Late Pledges common questions](https://help.kickstarter.com/en-us/articles/16236542-late-pledges-common-questions); enabling — [Help: How do I add late pledges to my project](https://help.kickstarter.com/en-us/articles/16236699-how-do-i-add-late-pledges-to-my-project)
- OFFICIAL (SNIPPET): Late-pledge fees equal campaign fees (5% + processing), charged only on successfully collected pledges. — [Help: Late Pledges common questions](https://help.kickstarter.com/hc/en-us/articles/24875599658779-Late-Pledges-Common-Questions)
- OFFICIAL (Valve): Steam keys are not for ongoing pre-release campaigns such as late pledges or "slacker backer" periods. — [Steamworks: Steam Keys](https://partner.steamgames.com/doc/features/keys)
- OFFICIAL (SNIPPET) — **Pledge Over Time:** backers split a pledge into 3 equal payments, first charge when the project ends successfully; no extra fees or third-party signup. Per-currency minimum pledge; **EUR 125.00** (also GBP 100, CAD 175, AUD 200, CHF 100). Rules in the FAQ apply to projects receiving their first Pledge Over Time payout on or after **15 Jan 2026**. — [Help: Pledge Over Time FAQ](https://help.kickstarter.com/en-us/articles/16236386-pledge-over-time-faq)
- OFFICIAL — **VAT in the Pledge Manager, change of 13 Apr 2026:** creators choose whether Kickstarter collects and remits VAT or they manage VAT outside Kickstarter; if they handle it themselves, VAT requirements such as tax IDs are removed, and **projects with VAT-inclusive pricing can now use the Pledge Manager**. Kickstarter continues to collect US/Canada taxes where required. — [Kickstarter release notes: VAT Flexibility in Pledge Manager](https://start.kickstarter.com/kickstarter-release-notes/vat-flexibility-in-pledge-manager)
- OFFICIAL (SNIPPET): Kickstarter collects VAT on EU/UK shipments when it is the deemed supplier or the creator authorises it; responsibility depends on ship-from location and value (EUR 150 / GBP 135 thresholds); tax is collected when backers complete the survey in the Pledge Manager; opting out leaves all VAT compliance with the creator; older FAQ text (requiring a VAT/OSS number and barring VAT-inclusive pricing) is superseded by the April 2026 note. — [Help: VAT collection in the pledge manager](https://help.kickstarter.com/en-us/articles/16236616-how-will-kickstarter-approach-vat-collection-in-the-pledge-manager); [Help: Understanding tax collection](https://help.kickstarter.com/en-us/articles/16236608-understanding-tax-collection-in-kickstarter-s-pledge-manager); [Help: Are creators based in the EU and UK responsible for VAT](https://help.kickstarter.com/en-us/articles/16236684-are-creators-based-in-the-eu-and-uk-responsible-for-vat); [Help: Pledge Manager Tax FAQ](https://help.kickstarter.com/en-us/articles/16236732-pledge-manager-tax-faq)
- OFFICIAL: The Pledge Manager can stay open to new backers after the campaign ends (SNIPPET). — [Kickstarter Updates: Built with creators](https://updates.kickstarter.com/built-with-creators-how-kickstarters-pledge-manager-has-grown-since-launch/)
- OFFICIAL — **Other 2026 release notes:** Add Free Items to Orders (19 Feb 2026); New Games & Publishing subcategories: TTRPG, STL, Audiobooks (8 May 2026); Mobile Discovery Mode (26 Aug 2026); Collaborator Access to Direct Messages (17 Sep 2026); Partial Refunds Now Support Decimal Amounts (25 Sep 2026). — [Kickstarter release notes](https://start.kickstarter.com/kickstarter-release-notes/vat-flexibility-in-pledge-manager)
- THIRD-PARTY: Projects We Love is a manual staff pick; there is no application, only a strong page helps. — [TCF](https://www.tcf.team/blog/kickstarter-review). Anecdote: one creator reported a 135% rise in daily pledges on the day of the badge. — [Stonemaier Games](https://stonemaiergames.com/?p=7154)
- THIRD-PARTY (unverified figure): about 80% of projects are accepted at review. — [TCF](https://www.tcf.team/blog/kickstarter-review)

### Inferences
- The VAT help articles are written around physical shipments. For **digital-only rewards** (Steam keys, cosmetics, credits) from a Netherlands individual, EU VAT on electronically supplied services is the creator's own matter; the April 2026 change means the creator can price VAT-inclusive and handle VAT outside Kickstarter. How Kickstarter's Pledge Manager treats digital-only rewards is not stated. Confirm with a Dutch tax adviser.
- In the Pledge Manager the processing fee is charged on tax too, so if Kickstarter collects VAT the creator pays roughly 3% + fixed fee on the VAT amount as well.
- Combining facts: because Valve bars keys for late-pledge periods before release and caps the campaign at 30 days, Late Pledges should stay off (or exclude key rewards) and the campaign should be 30 days or less, even though Kickstarter allows 60.
- Pledge Over Time is irrelevant below EUR 125, so only the top tiers would qualify.
- SEPA is unavailable for late pledges, which matters for a Dutch/EU backer base.

### Gaps
- **Fees for a Netherlands creator in EUR (5% + 3% + EUR 0.30; micro-pledge rate):** not re-confirmed; the official fees page was blocked and the help article gives only "roughly 3-5%". The previously known figure stands **unverified as of Nov 2026**.
- **Pre-launch page:** no source opened (what it shows, whether review must be passed first, follower notifications). Unverified.
- **Review time:** no official figure found.
- **Projects We Love criteria:** no official page found; third-party only.
- **Add-ons:** limits, image specs, and whether add-ons are available in-campaign vs. only in Pledge Manager — not verified beyond "optional extras" and their availability in late pledges.
- Whether Pledge Manager/Pledge Over Time are available to Netherlands/EUR creators specifically (EUR minimum listed suggests yes for Pledge Over Time): not explicitly confirmed.
- A help snippet says import duties may apply on delivery to EU backers from 1 July 2026 ([Help: tax expectations](https://help.kickstarter.com/en-us/articles/16236589-how-to-set-tax-expectations-with-your-backers)); not opened, and not relevant to digital-only rewards.

## 6. Valve/Steam side: keys for crowdfunding backers; Steam logo and badge usage

### Takeaway
No change found to the known key rules: confirmed Steam distribution first, 30-day maximum, no late-pledge/slacker-backer use, at most 2,500 beta (Release State Override) keys promised; additionally, do not use Steam logos until the product is under a Steam Distribution Agreement. The Steam logo may only come from Valve's approved artwork, must stand alone, must not be a prominent feature, and needs the legal attribution line.

### Cited Findings
- OFFICIAL: Keys may be promoted as crowdfunding rewards only for products with **confirmed Steam distribution** (Steamworks partner onboarding plus an App ID or credit). — [Steamworks: Steam Keys](https://partner.steamgames.com/doc/features/keys)
- OFFICIAL: **Do not use Valve or Steam logos for products not under a Steam Distribution Agreement.** — [Steamworks: Steam Keys](https://partner.steamgames.com/doc/features/keys)
- OFFICIAL: "You should not promise more than 2,500 Release State Override (beta) keys" in a campaign; such keys are "generally limited to 2,500 total"; "It is never OK to sell Release-State Override keys." Default Release keys can fulfil remaining backer rewards. — [Steamworks: Steam Keys](https://partner.steamgames.com/doc/features/keys)
- OFFICIAL: Campaigns must be time-limited, **maximum 30 days**; keys are not for ongoing pre-release campaigns such as late pledges or "slacker backer" periods. — [Steamworks: Steam Keys](https://partner.steamgames.com/doc/features/keys)
- OFFICIAL: Launching games may receive up to **5,000 Default Release keys**; more is case by case. New developers must wait **three weeks after creating an App ID** before requesting keys. Committing to a deal before keys are approved does not oblige Valve to provide them; "Valve reserves the right to reject Steam Key requests." — [Steamworks: Steam Keys](https://partner.steamgames.com/doc/features/keys)
- OFFICIAL: Parity: do not give Steam customers a worse deal than key purchasers; discounts elsewhere are fine if a comparable Steam offer follows within a reasonable time; free key giveaways need the same offer on Steam (under 100 keys excepted). Requests can be denied when keys requested are out of proportion to Steam interest. — [Steamworks: Steam Keys](https://partner.steamgames.com/doc/features/keys)
- OFFICIAL: Steam Playtest access must be free; never sell Playtest keys or bundle them in paid offers. Activating a key removes the game from that user's wishlist. — [Steamworks: Steam Keys](https://partner.steamgames.com/doc/features/keys)
- OFFICIAL — **Branding:** the Steam logo must not suggest Valve sponsorship, endorsement or affiliation; Steam trademarks cannot be a primary or prominent feature of non-Valve materials; **only approved Valve artwork**; the logo must stand alone, not combined with other logos, words, graphics or symbols; Valve may require changes/removal and reserves the right to approve communications using the Steam brand before distribution. — [Steamworks: Steam Branding Guidelines](https://partner.steamgames.com/doc/marketing/branding)
- OFFICIAL: Use the logo with the ® mark and include Valve's copyright/trademark attribution (dated ©2026 on the page; translations linked). The full **Steam Brand Guidelines PDF and logo files (EPS)** are downloaded from that page; brand guidelines last updated December 2024. — [Steamworks: Steam Branding Guidelines](https://partner.steamgames.com/doc/marketing/branding)
- OFFICIAL: Wherever Steam keys for an **Early Access** title are sold, an approved Steam Early Access logo and the required disclaimer text must be shown and the current state of the game clearly described. — [Steamworks: Steam Branding Guidelines](https://partner.steamgames.com/doc/marketing/branding)

### Inferences
- A "Wishlist on Steam" image on the Kickstarter page is permissible only once the game is under the Steam Distribution Agreement with an App ID (the precondition for promising keys anyway), and should be built from Valve's official logo artwork with clear space, not a restyled or recoloured logo merged into custom art. It should be one element in the page, not the dominant graphic, and the attribution line ("Steam and the Steam logo are trademarks and/or registered trademarks of Valve Corporation...") should appear in the story footer.
- If backers get a 2,500-or-fewer beta build, it must be framed as a reward for backing, with the release key as the main reward; larger tests should use Steam Playtest (free, not sellable as a tier perk).
- A Kickstarter price below the planned Steam launch price is compatible with Valve's parity rule only if a comparable offer (e.g. a launch discount) reaches Steam customers within a reasonable time.

### Gaps
- The branding page summary obtained does **not** mention ready-made "Wishlist on Steam" / "Available on Steam" badge art; whether Valve supplies such badges (as opposed to the bare logo in the EPS pack) and the exact clear-space/colour rules are in the Brand Guidelines PDF, which was **not opened**. Unverified.
- The exact attribution sentence was not captured verbatim; copy it from the branding page.
- Whether Valve's pre-distribution approval right is exercised for crowdfunding pages in practice: unknown.
