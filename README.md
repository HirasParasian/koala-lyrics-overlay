# Spotify Lyrics Overlay

Floating transparent lyrics overlay for Spotify on Windows.

## Features

- WPF .NET 8 (Windows 10 / 11)
- Native Windows Media Session (SMTC) integration — **No Spotify login or Client ID required**
- Real-time track detection & playback synchronization
- Synced lyrics via LRCLIB (exact match + fuzzy search fallback)
- Floating always-on-top transparent overlay
- Adjustable lyric font size & position lock
- Compact UI with clean layout
- Self-contained 1-click single-file installer

## Development & Run

```powershell
dotnet run
```

## Build 1-Click Installer

```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o publish
Copy-Item publish\SpotifyLyricsOverlay.exe dist\SpotifyLyricsOverlaySetup.exe
```

File `dist\SpotifyLyricsOverlaySetup.exe` dapat langsung dibagikan ke pengguna lain. Sekali klik, aplikasi langsung terinstall ke `%LOCALAPPDATA%\Programs\SpotifyLyricsOverlay\` dan shortcut dibuat di Desktop & Start Menu.
