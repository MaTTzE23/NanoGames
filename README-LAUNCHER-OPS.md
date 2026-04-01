# NanOnline Launcher Operations

Technische Admin-README fuer Launcher, Live-Client und Patch-Auslieferung.

## Pfade

- Launcher-Source: `C:\Users\Administrator\FiestaLauncher`
- Launcher-Projekt: `C:\Users\Administrator\FiestaLauncher\FiestaLauncher`
- Launcher-Publish-Output: `C:\Users\Administrator\FiestaLauncher\Build`
- Website-Root: `C:\inetpub\wwwroot\nano_site`
- Live-Patch-Client: `C:\inetpub\wwwroot\nano_site\downloads\client`
- Releases: `C:\inetpub\wwwroot\nano_site\downloads\releases`
- Manifest-Skripte: `C:\inetpub\wwwroot\nano_site\patcher-manifest`
- Game-Server: `C:\FiestaServer\Server`

## Wichtige Dateien

- Launcher UI: `FiestaLauncher/MainWindow.xaml`
- Launcher Window-Logik: `FiestaLauncher/MainWindow.xaml.cs`
- Patch-Logik: `FiestaLauncher/Services/PatchService.cs`
- Game-Start: `FiestaLauncher/Services/GameLauncher.cs`
- Launcher-Config: `FiestaLauncher/server.json`
- Build-Script: `build.bat`
- Manifest-Generator: `patcher-manifest/generate_manifest.ps1`
- Client-Sync: `patcher-manifest/sync_client_to_downloads.ps1`
- Release-Pakete: `packaging/build_client_packages.ps1`
- Manifest-Endpoint: `api/patch_manifest.php`
- File-Endpoint: `api/patch_file.php`

## Aktueller Live-Stand

- Launcher-Version: `1.2.0`
- Launcher-Dateiname: `NanOnlineLauncher.exe`
- Entry-Executable: `Nano.bat`
- Patch-Manifest: `C:\inetpub\wwwroot\nano_site\patcher-manifest\manifest.json`

## Launcher lokal bauen

Einfach:

```bat
C:\Users\Administrator\FiestaLauncher\build.bat
```

Direkt:

```powershell
dotnet publish C:\Users\Administrator\FiestaLauncher\FiestaLauncher\FiestaLauncher.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -o C:\Users\Administrator\FiestaLauncher\Build
```

## Launcher in Live-Client deployen

```powershell
$src = 'C:\Users\Administrator\FiestaLauncher\Build'
$dst = 'C:\inetpub\wwwroot\nano_site\downloads\client'
$files = @(
  'NanOnlineLauncher.exe',
  'D3DCompiler_47_cor3.dll',
  'PenImc_cor3.dll',
  'PresentationNative_cor3.dll',
  'sni.dll',
  'vcruntime140_cor3.dll',
  'wpfgfx_cor3.dll',
  'server.json'
)

foreach ($file in $files) {
  Copy-Item (Join-Path $src $file) (Join-Path $dst $file) -Force
}
```

Danach immer Manifest neu erzeugen.

## Manifest neu erzeugen

```powershell
powershell -ExecutionPolicy Bypass -File C:\inetpub\wwwroot\nano_site\patcher-manifest\generate_manifest.ps1 `
  -ClientRoot C:\inetpub\wwwroot\nano_site\downloads\client `
  -OutputPath C:\inetpub\wwwroot\nano_site\patcher-manifest\manifest.json `
  -Version 1.2.0 `
  -Channel live `
  -MinimumLauncherVersion 1.2.0 `
  -EntryExecutable Nano.bat
```

## Normale Client-Patches erstellen

Wenn du Dateien wie `Nano.bin`, `Nano.bat` oder whitelisted `ressystem`-Dateien aenderst:

1. Datei in `downloads/client` ersetzen.
2. Falls die Datei nicht in `generate_manifest.ps1` enthalten ist, in `$includedRelativePaths` aufnehmen.
3. Manifest neu erzeugen.
4. Launcher auf einem Testclient starten und Patch-Lauf pruefen.

## Launcher selbst patchen

Der Launcher patched sich selbst.

Wichtig:

- `NanOnlineLauncher.exe`
- `D3DCompiler_47_cor3.dll`
- `PenImc_cor3.dll`
- `PresentationNative_cor3.dll`
- `sni.dll`
- `vcruntime140_cor3.dll`
- `wpfgfx_cor3.dll`
- `server.json`

werden nicht blind ersetzt, sondern als `.next` gestaged und nach Launcher-Neustart ausgetauscht.

Das Verhalten kommt aus:

- `FiestaLauncher/Services/PatchService.cs`

## Release-Pakete bauen

```powershell
powershell -ExecutionPolicy Bypass -File C:\inetpub\wwwroot\nano_site\packaging\build_client_packages.ps1 `
  -SourceRoot C:\inetpub\wwwroot\nano_site\downloads\client `
  -OutputRoot C:\inetpub\wwwroot\nano_site\downloads\releases `
  -Version 1.2.0
```

Outputs:

- `C:\inetpub\wwwroot\nano_site\downloads\releases\NanOnline-Client-1.2.0.zip`
- `C:\inetpub\wwwroot\nano_site\downloads\releases\NanOnline-Client-Installer-1.2.0.exe`

Voraussetzung:

- `C:\Program Files\WinRAR\Rar.exe`

## Voller Release-Ablauf

1. Launcher-Code anpassen.
2. Launcher publishen.
3. Build-Dateien nach `downloads/client` kopieren.
4. Geaenderte Client-Dateien nach `downloads/client` kopieren.
5. Manifest neu erzeugen.
6. Endpunkte testen:
   - `https://patch.nanonline.net/api/patch_manifest.php`
   - `https://patch.nanonline.net/api/patch_file.php?path=Nano.bat`
7. Testclient patchen lassen.
8. ZIP + Installer bauen.

## Schnellchecks

Hash des Live-Launchers:

```powershell
Get-FileHash C:\inetpub\wwwroot\nano_site\downloads\client\NanOnlineLauncher.exe -Algorithm SHA256
```

Manifest anzeigen:

```powershell
Get-Content C:\inetpub\wwwroot\nano_site\patcher-manifest\manifest.json
```

Live-Release-Dateien anzeigen:

```powershell
Get-ChildItem C:\inetpub\wwwroot\nano_site\downloads\releases
```

## Haeufige Fehler

- Datei in `downloads/client` geaendert, aber Manifest nicht aktualisiert.
- Datei gepatched, aber nicht in `$includedRelativePaths`.
- Launcher gepublished, aber Runtime-DLLs nicht mit in den Live-Client kopiert.
- `server.json` im Build und im Live-Client nicht synchron.
- `EntryExecutable` im Manifest falsch.
- Paketbau gestartet, aber WinRAR fehlt.

## Hinweis

`net7.0-windows` baut aktuell noch, ist aber EOL. Fuer einen spaeteren Cleanup sollte der Launcher auf ein unterstuetztes .NET-Windows-Target gehoben werden.
