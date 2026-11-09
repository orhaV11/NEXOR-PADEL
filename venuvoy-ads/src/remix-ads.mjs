// Re-score the animated ads without re-rendering pictures: read each page's score (window.MUSIC) and cues,
// compose a sparse mix (only the hits that matter), master it to -14 LUFS / -1 dBTP, and swap the audio track.
//   node src/remix-ads.mjs [concept-filter]
import { createRequire } from "node:module";
import { spawnSync } from "node:child_process";
import { mkdirSync, readdirSync, renameSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { tmpdir } from "node:os";

const { chromium } = createRequire(import.meta.url)("playwright");
const SRC = dirname(fileURLToPath(import.meta.url));
const ADS = join(SRC, "..", "ads");
const FORMATS = { story: [1080, 1920], feed: [1080, 1350] };
const filter = process.argv[2] || "";
const run = (cmd, args) => { const r = spawnSync(cmd, args, { stdio: ["ignore", "inherit", "inherit"] }); if (r.status !== 0) throw new Error(cmd + " failed"); };

const browser = await chromium.launch();
for (const file of readdirSync(join(SRC, "concepts")).filter((f) => f.endsWith(".html") && f.includes(filter)).sort()) {
  const id = file.replace(/\.html$/, "");
  for (const [fmt, [w, h]] of Object.entries(FORMATS)) {
    const page = await browser.newPage({ viewport: { width: w, height: h } });
    await page.goto(pathToFileURL(join(SRC, "concepts", file)).href + "#" + fmt, { waitUntil: "load" });
    await page.evaluate(() => window.__ready);
    await page.evaluate(() => window.seek(0));
    const { dur, cues, music } = await page.evaluate(() => ({ dur: window.DURATION, cues: window.CUES, music: window.MUSIC }));
    await page.close();
    const tmp = join(tmpdir(), `vv-remix-${id}-${fmt}`);
    mkdirSync(tmp, { recursive: true });
    writeFileSync(join(tmp, "cues.json"), JSON.stringify(cues));
    run("python3", [join(SRC, "audio", "compose.py"), join(tmp, "raw.wav"), "--form", music.form, "--dur", String(dur), "--outro", String(music.outro), "--cues", join(tmp, "cues.json"), "--sparse"]);
    run("python3", [join(SRC, "audio", "master.py"), join(tmp, "raw.wav"), join(tmp, "music.wav")]);
    const mp4 = join(ADS, id, `${id}_${fmt}_${w}x${h}.mp4`);
    const next = join(tmp, "out.mp4");
    run("ffmpeg", ["-y", "-loglevel", "error", "-i", mp4, "-i", join(tmp, "music.wav"), "-map", "0:v", "-map", "1:a", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-shortest", "-movflags", "+faststart", next]);
    renameSync(next, mp4);
    rmSync(tmp, { recursive: true, force: true });
    console.log(`re-scored ${id} ${fmt}`);
  }
}
await browser.close();
