// Renders the approved editable SVG sources into the shared 48px PNG resources.
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
(async () => {
  const browser = await chromium.launch({headless: true, executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  try {
    const page = await browser.newPage({viewport: {width: 48, height: 48}, deviceScaleFactor: 1});
    const source = path.join(__dirname, 'BodyUrdfSource');
    for (const name of fs.readdirSync(source).filter(n => n.endsWith('.svg') && (!process.argv[2] || n === process.argv[2]))) {
      await page.setContent('<style>html,body{margin:0;width:48px;height:48px;background:transparent}svg{display:block;width:48px;height:48px}</style>' + fs.readFileSync(path.join(source,name),'utf8'));
      await page.screenshot({path:path.join(__dirname,name.replace(/\.svg$/,'.png')),omitBackground:true});
    }
  } finally { await browser.close(); }
})();
