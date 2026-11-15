# Which tools make the new series (tested 15 Nov 2026, same moment generated with each)

Test: two friends walk out of an airport, he says "We are actually here!", she laughs (and, where asked, answers
"Told you."). One start picture (nano-banana-pro, 2k, $0.022), then each video model. Judged on frame strips,
close crops of the faces, the words read back by the recogniser, and a pitch and speaker comparison of the voices.

| model | price | output | what came out |
|---|---|---|---|
| veo3.1-lite i2v ("1080p" asked) | $0.39 / 8 s | 720x1280 | lively; added words nobody wrote ("I hate to hear that") |
| veo3.1-fast i2v ("1080p" asked) | $0.78 / 8 s | 720x1280 | the two suitcases merged on one side; said the line twice |
| kling-v3.0-omni-pro i2v | $0.70 / 5 s | 1080x1920 | clean and sharp; no way to fix the voice |
| seedance-2 i2v fast | $0.75 / 5 s | 720x1280 | the liveliest movement; an arm morphs; wheels slide |
| seedance-2 omni reference fast (pictures + voice samples) | $0.75 / 5 s | 720x1280 | both lines right; voices only loosely like the samples |
| **kling-4.0-reference-to-video** (characters as "elements": 2-4 pictures + a voice sample each) | $0.785 / 5 s at 720p, **$1.175 / 5 s at 1080p** | up to 1080x1920, 3-30 s | both lines right, both people stay themselves, the voices match the samples (same pitch: 200 against 195 Hz, 262 against 262 Hz), no fault seen |

Decision: **Kling 4.0 reference-to-video at 1080p** for every shot. It is the only one that keeps both the face
and the VOICE of a recurring character from shot to shot, which is the fault the client heard in the first series.
A start picture is passed as `@image_1` ("Start exactly from @image_1"); characters as `elements` named in the
prompt with `@Name`. Veo does not deliver 1080p in 9:16 through this service, whatever is asked.

Other tools found: `nano-banana-pro` / `-edit` for start pictures; `suno-create-music` ($0.10) for the theme;
`gemini-3-1-flash-tts` for voice samples; `gemini-video-vision` ($0.004) watches a film WITH SOUND and answers
questions (src/film/watch.py) but was failing on the service's side during the test ("prepayment credits are
depleted" at the provider), so it cannot be relied on; `kling-4.0-flash-*` exists only at 720p.

Look test (the same two shots of the pilot in two looks, both Kling 4.0 at 1080p): `ads/17-pnp/look-test/`.
