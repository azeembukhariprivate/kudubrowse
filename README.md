# KUDUBROWSE

A private everyday web browser for Windows 10 and 11: every site works like it does in Chrome or Edge, without an
account and without anything about you being collected.

> **Being built.** KUDUBROWSE is being made step by step and isn't ready to download yet.
>
> 1. ✅ The browser itself: tabs, the address bar, back and forward, tabs that come back after a restart
> 2. Blocking ads and trackers
> 3. Passwords, and bringing everything over from Chrome
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
