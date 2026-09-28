# Vendored browser modules

Served as local ES modules (imported by `app/sharevideo.js` on demand, precached by `sw.js`). No CDN at runtime.
Each one is the package's own published build, copied unchanged, with its MIT license beside it.

| folder | package | version | source |
|---|---|---|---|
| `mp4-muxer/` | [mp4-muxer](https://github.com/Vanilagy/mp4-muxer) | 5.2.2 | `build/mp4-muxer.mjs` from the npm tarball |
| `webm-muxer/` | [webm-muxer](https://github.com/Vanilagy/webm-muxer) | 5.1.4 | `build/webm-muxer.mjs` from the npm tarball |

Both are by Vanilagy, MIT. They take the chunks a WebCodecs `VideoEncoder` produces and write an MP4 (H.264 on real
phones) or a WebM (VP9 / VP8 where H.264 is not available) in memory; the server never touches video.

To update: `npm pack mp4-muxer@<version>` (and `webm-muxer@<version>`), copy `package/build/<name>.mjs` and
`package/LICENSE` here, and bump the table.
