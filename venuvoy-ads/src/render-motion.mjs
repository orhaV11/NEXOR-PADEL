// Animated versions of the six ads: 8 s each, 9:16 and 4:5, with music and cue-placed effects.
//   node src/render-motion.mjs                    # all six, both formats
//   node src/render-motion.mjs kickoff story      # filter by concept and/or format
//   node src/render-motion.mjs kickoff story --stills 0.3,1.2,2.6
import { createRequire } from "node:module";
import { spawn, spawnSync } from "node:child_process";
import { mkdirSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { tmpdir } from "node:os";

const { chromium } = createRequire(import.meta.url)("playwright");
const SRC = dirname(fileURLToPath(import.meta.url));
const ADS = join(SRC, "..", "ads");
const FPS = 30;
const FORMATS = { story: [1080, 1920], feed: [1080, 1350] };
/** Where each ad's 8-second slice of the soundtrack starts (all on bar lines, none in the breakdown). */
const MUSIC_FROM = { "01-kickoff": 4, "02-sentence": 6, "03-alert": 8, "04-trust": 20, "05-shabbat": 22, "06-offer": 24 };

const pos = process.argv.slice(2).filter((a, i, all) => !a.startsWith("--") && all[i - 1] !== "--stills");
const [conceptFilter = "", formatFilter = ""] = pos;
const si = process.argv.indexOf("--stills");
const STILLS = si >= 0 ? process.argv[si + 1].split(",").map(Number) : null;

const run = (cmd, args) => {
  const r = spawnSync(cmd, args, { stdio: ["ignore", "inherit", "inherit"] });
  if (r.status !== 0) throw new Error(`${cmd} failed`);
};

const browser = await chromium.launch();
const concepts = readdirSync(join(SRC, "concepts")).filter((f) => f.endsWith(".html") && f.includes(conceptFilter)).sort();
for (const file of concepts) {
  const id = file.replace(/\.html$/, "");
  for (const [fmt, [w, h]] of Object.entries(FORMATS)) {
    if (formatFilter && !fmt.includes(formatFilter)) continue;
    const page = await browser.newPage({ viewport: { width: w, height: h }, deviceScaleFactor: 1 });
    await page.goto(pathToFileURL(join(SRC, "concepts", file)).href + "#" + fmt, { waitUntil: "load" });
    await page.evaluate(() => window.__ready);
    await page.evaluate(() => window.seek(0));
    const { dur, cues } = await page.evaluate(() => ({ dur: window.DURATION, cues: window.CUES }));
    if (STILLS) {
      const dir = join(ADS, id, "stills");
      mkdirSync(dir, { recursive: true });
      for (const t of STILLS) {
        await page.evaluate((t) => window.seek(t), t);
        await page.screenshot({ path: join(dir, `${fmt}-${t.toFixed(2)}.jpg`), type: "jpeg", quality: 80, clip: { x: 0, y: 0, width: w, height: h } });
      }
      console.log(`stills ${id} ${fmt}`);
      await page.close();
      continue;
    }
    const tmp = join(tmpdir(), `vv-${id}-${fmt}`);
    mkdirSync(tmp, { recursive: true });
    const silent = join(tmp, "video.mp4");
    const ff = spawn("ffmpeg", ["-y", "-loglevel", "error", "-f", "image2pipe", "-framerate", String(FPS), "-c:v", "mjpeg", "-i", "-",
      "-c:v", "libx264", "-preset", "slow", "-crf", "18", "-pix_fmt", "yuv420p", "-profile:v", "high", "-r", String(FPS), silent],
      { stdio: ["pipe", "inherit", "inherit"] });
    const frames = Math.round(dur * FPS);
    for (let i = 0; i < frames; i++) {
      await page.evaluate((t) => window.seek(t), i / FPS);
      const buf = await page.screenshot({ type: "jpeg", quality: 95, clip: { x: 0, y: 0, width: w, height: h } });
      if (!ff.stdin.write(buf)) await new Promise((r) => ff.stdin.once("drain", r));
    }
    ff.stdin.end();
    await new Promise((r, j) => ff.on("close", (c) => (c === 0 ? r() : j(new Error("ffmpeg " + c)))));
    await page.close();
    const cuesPath = join(tmp, "cues.json");
    writeFileSync(cuesPath, JSON.stringify(cues));
    const wav = join(tmp, "music.wav");
    const raw = join(tmp, "raw.wav");
    run("python3", [join(SRC, "audio", "compose.py"), raw, "--cut", String(MUSIC_FROM[id] ?? 4), String(dur), "--cues", cuesPath, "--sparse"]);
    run("python3", [join(SRC, "audio", "master.py"), raw, wav]);
    const out = join(ADS, id, `${id}_${fmt}_${w}x${h}.mp4`);
    run("ffmpeg", ["-y", "-loglevel", "error", "-i", silent, "-i", wav, "-map", "0:v", "-map", "1:a", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k",
      "-shortest", "-movflags", "+faststart", out]);
    rmSync(tmp, { recursive: true, force: true });
    console.log(`video ${out.replace(ADS + "/", "")}`);
  }
}
await browser.close();
