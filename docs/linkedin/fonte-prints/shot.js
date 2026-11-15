const { chromium } = require('playwright');
(async () => {
  const [base, out, dpr, ...telas] = process.argv.slice(2);
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 1280, height: 800 }, deviceScaleFactor: +dpr });
  for (const t of telas) {
    const [nome, q] = t.split('?');
    await p.goto(`file://${base}?tela=${nome}${q ? '&' + q : ''}`);
    await p.evaluate(() => document.fonts.ready);
    await p.waitForTimeout(300);
    await p.screenshot({ path: `${out}/${nome}${q?"-"+q.replace(/[^a-z0-9]/gi,""):""}.png` });
  }
  await b.close();
})();
