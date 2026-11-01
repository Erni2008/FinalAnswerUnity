const path=require('path'),cp=require('child_process');
const cli=path.join(path.dirname(require.resolve('playwright/package.json')),'cli.js');
const result=cp.spawnSync(process.execPath,[cli,'install','chromium'],{stdio:'inherit',env:{...process.env,PLAYWRIGHT_BROWSERS_PATH:path.join(__dirname,'.browsers')}});
process.exit(result.status??1);
