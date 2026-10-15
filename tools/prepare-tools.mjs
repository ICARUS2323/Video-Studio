import { mkdir, writeFile, readFile, rename } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { execFileSync } from 'node:child_process';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const target = path.join(root, 'vendor', 'tools');
const cache = path.join(root, 'vendor', 'downloads');
await mkdir(target, { recursive: true });
await mkdir(cache, { recursive: true });
const headers = { 'User-Agent': 'VideoStudio/1.0', Accept: 'application/vnd.github+json' };
async function json(url) {
  const r = await fetch(url, { headers });
  if (!r.ok) throw new Error(`${r.status}: ${url}`);
  return r.json();
}
async function download(asset, destination) {
  const expected = asset.digest?.replace(/^sha256:/, '');
  if (!expected || !/^[a-f0-9]{64}$/.test(expected)) throw new Error(`SHA256 bilgisi yok: ${asset.name}`);
  let bytes;
  try { bytes = await readFile(destination); } catch {}
  if (!bytes || createHash('sha256').update(bytes).digest('hex') !== expected) {
    console.log(`İndiriliyor: ${asset.name} (${Math.round(asset.size / 1048576)} MB)`);
    const r = await fetch(asset.browser_download_url, { headers });
    if (!r.ok) throw new Error(`${r.status}: ${asset.name}`);
    bytes = Buffer.from(await r.arrayBuffer());
    if (createHash('sha256').update(bytes).digest('hex') !== expected) throw new Error(`SHA256 doğrulanamadı: ${asset.name}`);
    await writeFile(destination + '.tmp', bytes);
    await rename(destination + '.tmp', destination);
  }
  console.log(`SHA256 doğrulandı: ${asset.name}`);
}
function unzip(archive, destination) {
  const quote = s => "'" + s.replaceAll("'", "''") + "'";
  const script = `$ErrorActionPreference='Stop'; Add-Type -AssemblyName System.IO.Compression.FileSystem; $archive=[IO.Compression.ZipFile]::OpenRead(${quote(archive)}); $dest=[IO.Path]::GetFullPath(${quote(destination)})+[IO.Path]::DirectorySeparatorChar; [IO.Directory]::CreateDirectory($dest)|Out-Null; try { foreach($entry in $archive.Entries) { $out=[IO.Path]::GetFullPath([IO.Path]::Combine($dest,$entry.FullName)); if(-not $out.StartsWith($dest,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid archive path'}; if($entry.Name){[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($out))|Out-Null; [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$out,$true)} } } finally {$archive.Dispose()}`;
  execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-EncodedCommand', Buffer.from(script, 'utf16le').toString('base64')], { stdio: 'inherit', windowsHide: true });
}
const records = [];
const ytdlp = await json('https://api.github.com/repos/yt-dlp/yt-dlp-nightly-builds/releases/latest');
const ya = ytdlp.assets.find(a => a.name === 'yt-dlp.exe');
await download(ya, path.join(target, 'yt-dlp.exe'));
records.push({ project: 'yt-dlp/yt-dlp-nightly-builds', version: ytdlp.tag_name, url: ya.browser_download_url, sha256: ya.digest });
const deno = await json('https://api.github.com/repos/denoland/deno/releases/latest');
const da = deno.assets.find(a => a.name === 'deno-x86_64-pc-windows-msvc.zip');
const dz = path.join(cache, da.name);
await download(da, dz);
unzip(dz, target);
records.push({ project: 'denoland/deno', version: deno.tag_name, url: da.browser_download_url, sha256: da.digest });
const ff = await json('https://api.github.com/repos/yt-dlp/FFmpeg-Builds/releases/latest');
const fa = ff.assets.find(a => a.name === 'ffmpeg-master-latest-win64-gpl.zip');
if (!fa) throw new Error('FFmpeg Windows paketi bulunamadı.');
const fz = path.join(cache, fa.name);
await download(fa, fz);
const unpacked = path.join(cache, 'ffmpeg');
unzip(fz, unpacked);
const { readdir, copyFile } = await import('node:fs/promises');
async function find(dir, name) {
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    const p = path.join(dir, entry.name);
    if (entry.isDirectory()) { const result = await find(p, name); if (result) return result; }
    else if (entry.name === name) return p;
  }
}
for (const name of ['ffmpeg.exe', 'ffprobe.exe']) {
  const source = await find(unpacked, name);
  if (!source) throw new Error(`${name} arşivde yok.`);
  await copyFile(source, path.join(target, name));
}
const licenseDir = path.join(target, 'licenses');
await mkdir(licenseDir, { recursive: true });
const ffLicense = await find(unpacked, 'LICENSE.txt');
if (ffLicense) await copyFile(ffLicense, path.join(licenseDir, 'FFmpeg-GPL.txt'));
for (const [name, url] of [
  ['yt-dlp-LICENSE.txt', 'https://raw.githubusercontent.com/yt-dlp/yt-dlp/master/LICENSE'],
  ['yt-dlp-THIRD-PARTY.txt', 'https://raw.githubusercontent.com/yt-dlp/yt-dlp/master/THIRD_PARTY_LICENSES.txt'],
  ['Deno-LICENSE.txt', 'https://raw.githubusercontent.com/denoland/deno/' + deno.tag_name + '/LICENSE.md']
]) {
  const response = await fetch(url, { headers });
  if (!response.ok) throw new Error(`License download failed: ${url}`);
  await writeFile(path.join(licenseDir, name), await response.text());
}
records.push({ project: 'yt-dlp/FFmpeg-Builds', version: ff.tag_name, url: fa.browser_download_url, sha256: fa.digest });
await writeFile(path.join(target, 'versions.json'), JSON.stringify(records, null, 2));
console.log('İndirme motorları hazır.');
