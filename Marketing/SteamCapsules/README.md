# REEF RUN — Steam capsule artwork

The complete Steam capsule set for **REEF RUN** (exact spelling: two words, all caps in the logotype).

**Chosen design:**
- **Art:** "Eclipse." Looking up from the kelp into the sun, the great white crosses in front of the light while the school scatters.
- **Title:** style `M3`. REEF RUN set exactly like the game's own main-menu logo: Barlow Condensed ExtraBold Italic, white REEF and teal RUN, the same tight spacing, measured against a live capture of the menu. On top of that it has a short 3D extrusion, a shark bite out of the N, and a waterline through both words.
- **Layout:** every format shows the **same content** (sun, shark, school, kelp, title), recomposed in-engine for its shape. The exception is the **small capsule**, which is deliberately simplified to the title and the shark so it reads at 231×87. The title is centred and spans **58% of the image width** in every format.

## Deliverables

| File | Size | Steam slot | Notes |
|---|---|---|---|
| `store/header_capsule_920x430.png` | 920×430 | Header capsule (required) | |
| `store/small_capsule_462x174.png` | 462×174 | Small capsule (required) | Simplified on purpose: the title and the shark only |
| `store/main_capsule_1232x706.png` | 1232×706 | Main capsule (required) | |
| `store/vertical_capsule_748x896.png` | 748×896 | Vertical capsule (required) | |
| `store/page_background_1438x810.png` | 1438×810 | Page background (optional) | Darkened and desaturated, no text |
| `library/library_capsule_600x900.png` | 600×900 | Library capsule (required) | |
| `library/library_header_920x430.png` | 920×430 | Library header (required) | Same art as the store header |
| `library/library_hero_3840x1240.png` | 3840×1240 | Library hero (required) | **No text**, as the rules require. The shark is in the centre safe area. |
| `library/library_logo_1280x429.png` | 1280×429 | Library logo (required) | Transparent PNG, logotype only |
| `master/master_keyart_3840x2160.png` | 3840×2160 | — | Master key art, no text |
| `master/master_keyart_logo_3840x2160.png` | 3840×2160 | — | Master key art with the title |
| `master/title_reefrun_4773x1600.png` | 4773×1600 | — | The title alone, transparent, high resolution |
| `editable/*.svg` | per format | — | Layered sources: art plate, scrim and title as separate layers |
| `previews/contact_sheet.png` | — | — | Every final asset on one sheet |
| `previews/display_sizes.png` | — | — | Each capsule at the size Steam shows it (1:1), plus stress tests |
| `previews/store_page_mock.png` | — | — | The capsules in a store-page layout on the page background |
| `previews/exploration/` | — | — | The design rounds that led here: concepts, shots, title styles, backgrounds |

All files are PNG, RGB except the library logo and title (RGBA). Every build checks each file programmatically: exact dimensions, colour mode, not blank, and that every layer an SVG links to exists.

**How the design was chosen** (boards in `previews/exploration/`):
1. Three key-art concepts: from above, inside the mouth, predator and prey.
2. Four title styles on a predator-and-prey shot, then four new shots with a centred title (Jaws Below, Flank Pass, Face-Off, Sunburst). You picked **Sunburst**.
3. Six letter treatments on the sunburst, then six structurally different styles (Cinematic, Surface split, Bitten, Coral, Plankton, Sticker) and four sunburst backgrounds (Sunburst, Deep, Eclipse, Rising).
4. You picked **Eclipse** with **Surface split + Bitten**.
5. **Font round** (`exploration/game_font/`): the title redone in the game's own logo lettering, compared against the live menu logo (`menu_title_reference.png`). You picked **M3**.

## Verified Steam requirements

Checked on 2026-10-16 against:
- partner.steamgames.com/doc/store/assets
- partner.steamgames.com/doc/store/assets/libraryassets
- partner.steamgames.com/doc/store/assets/rules

Sizes:
- Header 920×430, small 462×174, main 1232×706 and vertical 748×896 (all required). Page background 1438×810 (optional).
- Library capsule 600×900, library header 920×430, library hero 3840×1240 and library logo 1280 wide and/or 720 tall (all required). The logo must be a transparent PNG.

Content rules:
- Base capsules may contain only game artwork, the game name and any official subtitle. No review scores, awards, discount text, calls to action or other text. The product name must be readable.
- The library hero may contain no text at all. Its centre (860×380 in Steam's terms) stays uncropped as the client resizes.
- The library logo is the logotype only, on transparency. Its position over the hero is chosen in Steamworks after upload. **Use "left bottom" or "centered bottom"** so it doesn't cover the shark in the hero's centre.
- All artwork must be PG-13.

None of the capsules contain awards, scores, platform logos or claims.

## How it's made / how to change it

1. **Art plates.** In Unity, choose Tools > Marketing > **Capture Capsule Plates**. It opens `Assets/Scenes/MarketingShowcase.unity`, enters Play mode and renders every plate shot (the `Plate - …` children of *Marketing Showcase*) to `plates/` at its exact capsule size. The bodies, school, kelp, light and water are the game's own. Each plate is a normal `ShowcaseShot`: move its camera with "Set From Scene View", or move its actor marks.
2. **Title and capsules.** From `source/`, run:

   ```
   python compose_capsules.py
   ```

   This adds the vignette and title scrim, places the title using the `LAYOUTS` table (title height and vertical centre per format), and writes every file above, including the previews. It needs Pillow and nothing else, and takes about 15 s.
3. **The title** comes from `source/reefrun_gametitle.py` (style `M3`). It reproduces the game's logo settings from `Assets/UI Toolkit/ReefRunMainMenu.uss` (`.mm-logo-word`) and adds the depth, bite and waterline. M0 (exactly the game's logo), M1 (with depth) and M2 (bite and waterline, flat) are in the same file. The earlier explored styles (A–L, HI) are in `reefrun_logo3d.py` and `reefrun_letters.py`.
4. **Editable layers.** Each `editable/*.svg` stacks the plate, the scrim and the title PNG as separate layers, for any SVG editor (Inkscape, Illustrator, Figma import). The shipped PNGs come from the script, not the SVGs.

## Sources and licensing

- **Art:** in-engine renders of REEF RUN's own scene and assets. These are the great white shark from the "Underwater life deluxe" asset pack (already licensed for the game), the game's fish, kelp and HDRP water, and the game's BoidsManager flocking. Check that pack's licence terms allow marketing use of renders, as they do for the shipped game. No third-party stock, AI-generated or web imagery is used.
- **Fonts:** Barlow Condensed ExtraBold Italic / ExtraBold and Barlow SemiBold (SIL Open Font License 1.1; `source/fonts/OFL.txt`). The game's UI already uses them. The OFL allows embedding in images and logos.
- **Textures in the title:** the caustics and coral textures in the explored styles (B, J) were taken from the game's own screenshots. The chosen style `M3` uses none.

## Known limitations

- The game's seabed is regenerated each run, so re-rendered plates can differ slightly in kelp and coral placement. Within one Editor session they are reproducible.
- The SVGs are layered, not fully vector: the 3D title is a raster layer, because its extrusion, bite and waterline ripple are pixel effects.
- Steam generates its own half-size library variants (300×450, 1920×620) from these uploads. Check them in the Steamworks preview tool after upload.
