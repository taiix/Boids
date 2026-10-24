// @ts-check
// The Reef Run site: one static page for GitHub Pages (reefrun.stanmakes.games until the own domain
// is set). `npm run build` writes plain files to dist/, with the CNAME and .nojekyll Pages needs.
import tailwindcss from "@tailwindcss/vite";
import { defineConfig } from "astro/config";

const domain = "reefrun.stanmakes.games";

export default defineConfig({
  site: `https://${domain}`,
  output: "static",
  vite: { plugins: [tailwindcss()] },
  integrations: [
    {
      name: "github-pages-files",
      hooks: {
        "astro:build:done": async ({ dir }) => {
          const { writeFile } = await import("node:fs/promises");
          await writeFile(new URL("CNAME", dir), `${domain}\n`);
          // Jekyll would hide Astro's _astro/ folder.
          await writeFile(new URL(".nojekyll", dir), "");
        },
      },
    },
  ],
});
