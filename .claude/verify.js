// .claude/verify.js — das EINE Prüfkommando dieses Projekts.
//
// Vertrag (global, siehe ~/.claude/CLAUDE.md, 'Verify-Konvention'):
//   exit 0        = Build UND Tests grün
//   exit != 0     = kaputt; stdout/stderr enthalten den ersten echten Fehler
//   keine Argumente nötig, läuft aus dem Repo-Wurzelverzeichnis
//
// Projekt: ASP.NET-Core-Server (net10.0) + xUnit-Testprojekt.
// Der System-'dotnet' hat kein SDK für net10.0 — immer den benutzereigenen unter ~/.dotnet nehmen.
// NuGet.config im Repo klemmt die Firmenquelle ab (sonst 401 beim Restore).

const { spawnSync } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');

const root = path.resolve(__dirname, '..');
const dotnet = path.join(os.homedir(), '.dotnet', process.platform === 'win32' ? 'dotnet.exe' : 'dotnet');

if (!fs.existsSync(dotnet)) {
  console.error(`verify.js: ${dotnet} nicht gefunden`);
  process.exit(2);
}

function run(args, what) {
  const r = spawnSync(dotnet, args, { cwd: root, stdio: 'inherit' });
  if (r.status !== 0) {
    console.error(`verify.js: ${what} fehlgeschlagen`);
    process.exit(r.status || 1);
  }
}

run(['build', 'Echolines.sln', '--nologo'], 'Build');
run(['test', 'Echolines.sln', '--no-build', '--nologo'], 'Tests');

console.log('verify.js: grün (Build + Tests)');
process.exit(0);
