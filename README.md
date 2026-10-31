<p align="center">
  <img src="docs/logo-512.png" alt="KUDUBROWSE logo" width="128">
</p>

<h1 align="center">KUDUBROWSE</h1>

<p align="center">A private everyday web browser for Windows. Every site works as it does in Chrome or Edge, with ads, trackers, cookie banners and pop-ups blocked, a built-in password manager, and rooms that keep your logins apart. No account, and nothing about you is collected. Free and open source.</p>

<p align="center">
  <a href="https://github.com/azeembukhariprivate/kudubrowse/releases/latest/download/KUDUBROWSE-setup.exe"><img src="https://img.shields.io/badge/Download-KUDUBROWSE%20for%20Windows-0E8C7E?style=for-the-badge" alt="Download KUDUBROWSE for Windows"></a>
  <br>
  <sub>The installer for Windows 10 and 11, always the latest version · <a href="https://github.com/azeembukhariprivate/kudubrowse/releases/latest">portable zip and what's new</a></sub>
</p>

---

## What makes it different

- **Rooms**: keep separate logins side by side, like your personal and work email, each in its own room with its own colour. Sites in one room can't see the cookies and logins of another. Start with Personal and Work, and add as many as you like.
- **No account, nothing collected**: KUDUBROWSE sends nothing about you anywhere. Your tabs, history, bookmarks and passwords stay on your PC.
- **Ads, trackers, cookie banners and pop-ups blocked**, built in, with nothing to add. It uses the public EasyList, EasyPrivacy and EasyList Cookie List, fetched again every few days. The shield button shows what was stopped on the page, and lets a site off if it doesn't work properly.
- **Passwords filled only when you pick them**: a saved login goes into a page only after you choose it from the browser's own list next to the box, so a page can't take your passwords. They're locked to your Windows account, and showing or copying one asks for Windows Hello (or your Windows password) first.
- **Leaked password check**: finds saved passwords that have turned up in known data leaks. Only the first 5 characters of a scrambled copy of each password are sent, never the password itself.
- **Moving from Chrome**: brings over your bookmarks and history, and your passwords from the file Chrome saves them to (the file is deleted afterwards). Chrome's own files are only read, never changed.
- **Sleeping tabs**: tabs you haven't looked at for 10 minutes rest and use almost no processor and less memory. They wake up when you click them; tabs playing sound stay awake.

## Everyday features

- **Tabs**: open, close and reopen closed tabs (Ctrl+Shift+T), switch with Ctrl+Tab or Ctrl+1 to Ctrl+9. The tabs you had open come back next time and load when you first click them, so starting stays quick.
- **Tab groups**: put tabs in a named, coloured group and fold it away to just its name (right-click a tab).
- **Two pages side by side** in one window, with a line between them you can drag (right-click a tab).
- **The address bar**: type an address or words to search for, with Google, Bing, DuckDuckGo, Brave Search or Startpage. It suggests pages from your bookmarks and history as you type.
- **Bookmarks** (the star, Ctrl+D) with folders and a bookmarks bar, and **history** by day, kept for 90 days. Both can be searched (Ctrl+Shift+O, Ctrl+H).
- **Reading mode** (F9): just the article as clean text, in light, sepia or dark, at the text size you like.
- **A picture of the whole page**, not just the part on screen (Ctrl+Shift+S), and **saving a page as PDF**.
- **Private windows** (Ctrl+Shift+N): no history is kept, and sites' cookies are forgotten when the window closes.
- **A download manager** (Ctrl+J). When a download starts, a small note under the downloads button says so; click it to see your downloads.
  - **Big files come faster**: files over 20 MB are fetched in up to six pieces at the same time when the site allows it, with your login for that site, and get the same "from the internet" mark and virus check as other downloads.
  - **Pause and carry on**, even after closing the browser or restarting the PC; **try again** when a download stops part way, from where it stopped when the site allows it. Dropped connections are tried again by themselves.
  - **The list is kept** for next time (not for private windows), with the speed and the time left while files arrive.
  - **Choose where files go**: a folder of your own, ask each time, or **sorted into folders by type** (Pictures, Music, Videos, Documents, Archives, Programs). A file already there is never replaced.
  - Files that can run programs ask before opening.
- **Clear a site's cookies and data** from the shield button, or the history and every room's site data from Settings.
- **Default browser**: can be chosen in Windows' Default apps, and links clicked in other apps open as a new tab in the browser that's already open.
- **About KUDUBROWSE** (in the menu) has everything in one place: who made it, its version and updates, and all of the above with a "Try it" button for each.
- **Updates itself**: once a day it looks for a new version, downloads it quietly, checks it against GitHub's checksum, and installs it when you close the browser; the next time you open it, it says what it was updated to. It can be switched off in Settings. (A copy run from the portable zip isn't changed; it just says when a new version is out.)
- Zoom, print, full screen (F11), and light and dark looks that follow Windows.

## Download

**[Download the installer](https://github.com/azeembukhariprivate/kudubrowse/releases/latest/download/KUDUBROWSE-setup.exe)** (`KUDUBROWSE-setup.exe`, always the latest version), or get any of these from the [Releases page](https://github.com/azeembukhariprivate/kudubrowse/releases/latest):

- `KUDUBROWSE-x.y.z-setup.exe` installs KUDUBROWSE for your Windows account (no administrator rights needed to install).
- `KUDUBROWSE-x.y.z-portable.zip` runs without installing: unzip it anywhere and open `KUDUBROWSE.exe`.

Requires Windows 10 or 11. Pages are shown by Microsoft Edge WebView2, the engine that is part of Windows 10 and 11 and is kept up to date by Windows (on older copies of Windows 10 it can be [installed for free from Microsoft](https://go.microsoft.com/fwlink/p/?LinkId=2124703); the installer tells you if it's missing). Uninstalling asks before removing your bookmarks, history, passwords and settings.

## Privacy

KUDUBROWSE itself collects nothing and has no account. Apart from the sites you visit, it only connects to:

- the block lists' websites, every few days, to fetch new copies of the lists;
- the sites you download big files from, to fetch them in pieces (with only that site's own cookies);
- Pwned Passwords, only when you press *Check for leaks* (5 characters of each scrambled password are sent);
- the search engine you chose, when you search from the address bar;
- GitHub, once a day, to look for a newer version and download it (can be turned off in Settings).

## Build from source

You need the [.NET SDK](https://dotnet.microsoft.com/download) (8 or later). The screens are built in C# and the themes are loaded at startup, so the project builds with `dotnet` on any operating system; the app itself runs on Windows.

```
dotnet run --project tests/KUDUBROWSE.Tests -c Release
dotnet build src/KUDUBROWSE/KUDUBROWSE.csproj -c Release -o out/KUDUBROWSE
out\KUDUBROWSE\KUDUBROWSE.exe --self-test        (Windows only)
```

The tests check the address bar, saving tabs (with their rooms and groups), the ad blocker (with made-up rules and the real lists, when the build has downloaded them into `src/KUDUBROWSE/Lists`), bookmarks, history, suggestions, the download manager (against a small web server on the computer that serves files in pieces, slowly, or dropping the connection), reading Chrome's bookmarks and history (from a made-up Chrome folder in `tests/KUDUBROWSE.Tests/Fixtures`), passwords and the leak check, rooms, and the one-running-copy hand-over. `--self-test` opens the real browser with test pages it makes itself, using its own profile, and uses it: addresses, links, back and forward, pop-ups, closed tabs, zoom, blocking, history, bookmarks, bringing things from Chrome, saving and filling passwords, rooms and private windows, downloads (a big file in pieces, paused and carried on, the site's cookies, the "from the internet" mark, sorting, trying again after a restart), sleeping tabs, tab groups, side by side, reading mode, the whole-page picture and PDF, the default browser, tabs coming back and every window in both themes; it writes `self-test.log` and exits. To build the installer, install [Inno Setup 6](https://jrsoftware.org/isinfo.php) and compile `installer/KUDUBROWSE.iss`.

### Shared code

Some files (the look, dialogs, the update check, administrator helpers) are shared by the KUDU apps and come from
[kudu-common](https://github.com/azeembukhariprivate/kudu-common); each starts with a comment saying so, and `.kudu-common`
lists them. You don't need kudu-common to build this app, but change those files there, not here: the build checks that
they match.

## Publishing a new version

1. Change `<Version>` in `src/KUDUBROWSE/KUDUBROWSE.csproj` (and `MyAppVersion` in `installer/KUDUBROWSE.iss`).
2. Commit and push to `main`.
3. GitHub Actions tests and builds the app. When no release exists yet for that version, it creates the `v` tag and publishes the installer and portable zip on the Releases page. Pushes that keep the same version only build.

## License

[MIT](LICENSE) · Made by Azeem, with Claude and ♥. See [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

KUDUBROWSE is not affiliated with Microsoft or Google. Edge, WebView2 and Windows are names of Microsoft Corporation; Chrome is a name of Google LLC.
