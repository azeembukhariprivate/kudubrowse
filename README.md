# KUDUBROWSE

A private everyday web browser for Windows 10 and 11: every site works like it does in Chrome or Edge, without an
account and without anything about you being collected.

> **Being built.** KUDUBROWSE is being made step by step and isn't ready to download yet.
>
> 1. ✅ The browser itself: tabs, the address bar, back and forward, tabs that come back after a restart
> 2. ✅ Blocking ads, trackers, cookie banners and pop-ups
> 3. ✅ Bookmarks, history, suggestions, passwords, bringing everything from Chrome, default browser
> 4. Rooms (separate logins), reading mode, screenshots, and the installer
>
> Version 1.0 will be released when all four are done and tested.

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
