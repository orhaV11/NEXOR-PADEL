// Seeds a running OREVOSH instance with demo people, a brand, looks, tags, fires, follows and a hashtag challenge,
// so a screen has something to show. Usage: node seed.js http://127.0.0.1:5088   (the Anthropic API must be stubbed
// or real; the photos are generated here). Prints the handles it created. Safe to run more than once: handles get a suffix.
const { chromium } = require('playwright');
const base = process.argv[2] || 'http://127.0.0.1:5088';
const suffix = process.argv[3] || '';
const H = { 'X-Requested-With': 'Orevosh', 'Accept-Language': 'en' };

async function main() {
  const browser = await chromium.launch(process.env.CHROMIUM_PATH ? { executablePath: process.env.CHROMIUM_PATH } : {});
  const painter = await browser.newPage();
  const photo = (seed) => painter.evaluate((seed) => {
    const c = document.createElement('canvas'); c.width = 900; c.height = 1125;
    const g = c.getContext('2d');
    const hues = [[seed * 47 % 360, 30, 88], [(seed * 47 + 40) % 360, 35, 40]];
    g.fillStyle = `hsl(${hues[0][0]} ${hues[0][1]}% ${hues[0][2]}%)`; g.fillRect(0, 0, 900, 1125);
    for (let i = 0; i < 60; i++) { g.fillStyle = `hsl(${(seed * 31 + i * 23) % 360} 45% ${35 + (i % 40)}%)`; g.fillRect((i * 173 + seed * 11) % 800, (i * 257 + seed * 7) % 1000, 120 + (i % 5) * 20, 180 + (i % 3) * 30); }
    g.fillStyle = `hsl(${hues[1][0]} ${hues[1][1]}% ${hues[1][2]}%)`; g.fillRect(300, 250, 300, 620);
    return c.toDataURL('image/jpeg', 0.9).split(',')[1];
  }, seed).then((b64) => Buffer.from(b64, 'base64'));

  const people = [];
  async function person(handle, opts) {
    const ctx = await browser.newContext();
    const r = ctx.request;
    const h = handle + suffix;
    const me = await r.post(base + '/api/auth/signup', { headers: H, data: { handle: h, password: 'password123', birthDate: '1990-01-01', language: opts.language || 'en' } });
    if (!me.ok()) throw new Error('signup ' + h + ' ' + me.status() + ' ' + await me.text());
    if (opts.brand || opts.name || opts.bio || opts.interests) {
      await r.patch(base + '/api/users/me', { headers: H, data: { accountType: opts.brand ? 'Brand' : 'Person', displayName: opts.name, bio: opts.bio, website: opts.website, interests: opts.interests } });
    }
    await r.post(base + '/api/users/me/avatar', { headers: H, multipart: { image: { name: 'a.jpg', mimeType: 'image/jpeg', buffer: await photo(handle.length * 7 + 3) } } });
    const p = { handle: h, r, ctx, posts: [] };
    people.push(p);
    return p;
  }
  async function look(p, intent, caption, seed) {
    const check = await p.r.post(base + '/api/checks', { headers: H, multipart: { intent, occasion: '', language: 'en', image: { name: 'outfit.jpg', mimeType: 'image/jpeg', buffer: await photo(seed) } } });
    if (!check.ok()) throw new Error('check ' + check.status() + ' ' + await check.text());
    const c = await check.json();
    if (c.status !== 'ok') return null;
    const post = await p.r.post(base + '/api/posts', { headers: H, data: { checkId: c.id, caption } });
    if (!post.ok()) throw new Error('post ' + post.status() + ' ' + await post.text());
    const dto = await post.json();
    p.posts.push(dto.id);
    return dto;
  }

  const nexor = await person('nexor', { brand: true, name: 'NEXOR', bio: 'Slow fashion, fast opinions. Tel Aviv.', website: 'https://nexor.example' });
  const noa = await person('noa', { name: 'Noa', bio: 'Vintage hunter. Mostly black.', interests: ['Date', 'Streetwear'] });
  const dan = await person('dan', { name: 'Dan', bio: 'Office by day, courtside by night.', interests: ['Office', 'Sport'], language: 'he' });
  const maya = await person('maya', { name: 'Maya', bio: 'Old money on a student budget.', interests: ['OldMoney', 'Minimal'] });

  const ch = await nexor.r.post(base + '/api/challenges', { headers: H, data: { title: 'Date night in black', brief: 'All-black date looks. Texture over logos.', intent: 'Date', prize: 'A black shirt of your choice', prizeUrl: 'https://nexor.example/shirt', endsAt: new Date(Date.now() + 5 * 86400000).toISOString() } });
  const challenge = ch.ok() ? await ch.json() : null;
  const tag = challenge ? '#' + challenge.tag : '#datenight';

  await look(noa, 'Date', `Dinner fit, thoughts? ${tag} @${nexor.handle} #allblack`, 1);
  await look(noa, 'Streetwear', 'Saturday errands #streetstyle #vintage', 2);
  await look(dan, 'Office', 'Monday, but make it linen #office #linen', 3);
  await look(dan, 'Sport', 'Courtside #courtside @' + nexor.handle, 4);
  await look(maya, 'OldMoney', 'Thrifted the whole thing #oldmoney #thrift', 5);
  await look(maya, 'Date', `Trying the ${tag} thing #allblack`, 6);
  await look(nexor, 'Minimal', 'The linen shirt, back in stock #linen', 7);

  for (const [from, to] of [[noa, nexor], [dan, nexor], [maya, nexor], [dan, noa], [maya, noa], [noa, maya]]) await from.r.post(base + `/api/users/${to.handle}/follow`, { headers: H });
  for (const p of people) for (const other of people) if (other !== p) for (const id of other.posts.slice(0, 2)) await p.r.post(base + `/api/posts/${id}/fire`, { headers: H });
  await dan.r.post(base + `/api/posts/${noa.posts[0]}/comments`, { headers: H, data: { text: 'The boots make it.' } });
  await nexor.r.post(base + `/api/posts/${noa.posts[0]}/feature`, { headers: H });
  if (challenge) for (const voter of [dan, maya]) await voter.r.post(base + `/api/challenges/${challenge.id}/vote`, { headers: H, data: { postId: noa.posts[0] } });

  console.log(JSON.stringify({ base, handles: people.map((p) => p.handle), password: 'password123', challenge: challenge && challenge.id, tag }));
  await browser.close();
}
main().catch((e) => { console.error(e); process.exit(1); });
