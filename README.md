# KUDUBROWSE

A private everyday web browser for Windows 10 and 11: every site works like it does in Chrome or Edge, without an
account and without anything about you being collected.

> **Being built.** KUDUBROWSE is being made step by step and isn't ready to download yet.
>
> 1. ✅ The browser itself: tabs, the address bar, back and forward, tabs that come back after a restart
> 2. ✅ Blocking ads, trackers, cookie banners and pop-ups
> 3. ✅ Bookmarks, history, suggestions, passwords, bringing everything from Chrome, default browser
> 4. ✅ Rooms (separate logins), private windows, downloads, sleeping tabs, one running copy, tab groups,
>    side-by-side pages, reading mode, screenshots and PDF, and the installer
>
> Version 1.0 comes next, once this last part has been checked.

Part of the KUDU family of free, open-source Windows apps by Azeem Bukhari.

## What works so far

- Tabs: open, close, reopen closed tabs (Ctrl+Shift+T), switch with Ctrl+Tab or Ctrl+1 to Ctrl+9.
- Type a web address or words to search for (Google, Bing, DuckDuckGo, Brave Search or Startpage).
- Links that open a new window open in a new tab next to the page.
- The tabs you had open come back next time; they load when you first click them, so starting stays quick.
- Zoom, print, full screen (F11), light and dark look.
- Ads and trackers are blocked, cookie banners hidden and pop-ups that pages open by themselves stopped, using the
  public EasyList, EasyPrivacy and EasyList Cookie List (fetched again every few days). The shield button shows how
  much was blocked and can switch blocking off for a site.
- Bookmarks (the star, a bookmarks bar with folders), history by day, both searchable (Ctrl+H, Ctrl+Shift+O).
- The address bar suggests bookmarks and pages you visit often as you type.
- "Bring bookmarks and history from Chrome" (Chrome's files are only read), and a welcome window the first time.
- Can be chosen as the default browser in Windows' Default apps.
- Passwords: offered to save when you sign in, filled when you pick a login from the list next to the box, strong ones
  suggested when you sign up. They're encrypted for your Windows account; showing or copying one asks for Windows Hello
  (or your Windows password). "Check for leaks" sends only 5 characters of a scrambled code of each password.
  Passwords come from Chrome through the file Chrome exports, which is deleted afterwards.
- Rooms: tabs in different rooms (Personal, Work, any you add) keep their logins and site data apart, shown by a
  coloured line on the tab. Private windows (Ctrl+Shift+N) keep no history and forget sites' cookies when closed.
- Downloads in the browser's own list (Ctrl+J); files that can run programs ask before opening.
- Tabs not looked at for 10 minutes go to sleep and wake when clicked; a site's cookies and data can be cleared from
  the shield button.
- Links clicked in other apps open as a new tab in the browser that's already open.
- Tab groups: put tabs in a named, coloured group and fold it away to just its name (right-click a tab).
- Two pages side by side in one window, with a line between them you can drag (right-click a tab).
- Reading mode (F9): just the article as clean text, in light, sepia or dark, with the text size you like.
- A picture of the whole page, not just the part on screen (Ctrl+Shift+S), and saving a page as PDF.
- "What KUDUBROWSE can do" (in the menu and the About window) lists what makes it different and its best everyday
  features, each with a "Try it" button.
- The installer checks the Edge engine is there, and uninstalling asks before removing your bookmarks, history and
  passwords.

The pages are shown by Microsoft Edge WebView2, the engine that comes with Windows and is kept up to date by Windows.

## Shared code

The look and controls and a few helpers come from [kudu-common](https://github.com/azeembukhariprivate/kudu-common);
the build checks that they match.

## Build it yourself

You need the [.NET SDK](https://dotnet.microsoft.com/download) (8 or newer).

```
dotnet run --project tests/KUDUBROWSE.Tests -c Release
dotnet build src/KUDUBROWSE/KUDUBROWSE.csproj -c Release -o out/KUDUBROWSE
out\KUDUBROWSE\KUDUBROWSE.exe --self-test        (Windows only)
```

## Licence

[MIT](LICENSE) · Made by Azeem, with Claude and ♥
