# Spotify Lyrics Overlay `v1.0.4`

Floating transparent lyrics overlay for Spotify on Windows.

## Features

- **WPF .NET 8** (Windows 10 / 11)
- **Native Windows Media Session (SMTC)** integration — No Spotify login or Client ID required
- **Real-time track detection & playback synchronization**
- **Multi-Source Lyrics Fallback**:
  - Local custom lyrics (`.lrc` & `.txt`)
  - LRCLIB (synced karaoke LRC & plain text)
  - Kugou Music (synced Asian & international catalog)
  - Plain lyrics (full scrollable display with smooth manual scrolling)
- **Built-in Bundled Lyrics**: Otomatis mengekstrak koleksi lirik lagu populer saat pertama kali dipasang
- **One-Click Lyrics Folder Access**: Tombol khusus di pojok atas overlay untuk membuka folder lirik di Windows Explorer
- **Floating Always-On-Top** transparent overlay dengan font size slider & position lock
- **Crash Prevention Protection**: Dilengkapi global exception handler yang tahan terhadap gangguan koneksi media
- **Self-contained 1-click single-file installer**

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
