# WikiViewer

<img width="2366" height="1383" alt="Desktop - Home Page" src="https://github.com/user-attachments/assets/6f8ea6f7-e705-4044-a132-2cbfa1eb2495" />

**WikiViewer** is an unofficial, third-party client for browsing any MediaWiki-powered site - from Wikipedia to Fandom and beyond, without your web browser. It provides a clean, native, and performance-tuned experience for Windows, designed to be fast and responsive.

**Disclaimer:** This is an unofficial, third-party client for browsing MediaWiki sites. This app was created by [MegaBytesMe](https://github.com/megabytesme) and is not affiliated with, endorsed, or sponsored by the operators of any specific wiki. All article data, content, and trademarks are the property of their respective owners and contributors. This app simply provides a native viewing experience for publicly available content.

## Features

* **Universal Wiki Support:** Connect to your favorite MediaWiki site by simply adding the URL in the settings. From Wikipedia to niche wikis, enjoy them all in one app.
* **Clean \& Native Interface:** Experience a fast, fluid, and ad-free reading experience designed for Windows, free from the clutter of a standard web browser.
* **Advanced Offline Caching:** Save articles and all their media directly to your device. The intelligent caching system ensures you can continue reading even when you're without an internet connection.
* **Full Account Integration:** Sign in with your wiki account to edit pages, manage your watchlist, and contribute to the community directly from the app.
* **Favourites \& Watchlist Sync:** Keep a list of your most-visited articles. When logged in, this list automatically syncs with your official wiki watchlist.
* **Smart Search with Suggestions:** Find articles quickly with a built-in search bar that provides instant suggestions as you type, powered directly by the MediaWiki API.
* **Discover Something New:** Jump to a random article with a single click and explore the depths of your favorite topics.
* **Privacy-Focused by Design:** Your data is your own. The app collects no personal information, and all caches, favourites, and credentials are stored securely and only on your local device.

## Native reader preview

Open an article and select **Native reader (Preview)** in its command bar to switch between native UWP controls and Web view. Enable **Settings > Reading > Use native wikitext reader by default** to open articles in native mode automatically. The saved preference applies in both UWP heads and persists across app restarts; the article's reader button remains a temporary override. Web view is the initial default until the preference is enabled. Both UWP heads fetch raw wikitext, server-expanded HTML and the revision ID in one `action=parse` response, and cache them together. Templates, parser functions, Lua modules and wiki extensions are expanded by the wiki's server. Native controls render the expanded content, supplemented by placement and sizing hints parsed from the raw source. This is a combined source pipeline; it does not evaluate templates locally.

The reader supports paragraphs, headings, text formatting, internal/external links, citation anchors, images, nested lists and scrollable tables with spans. It preserves source order, captions, infoboxes, thumbnail placement, declared dimensions, alignment, colours, borders, spacing, columns and collapsible sections where native controls can express them. Expanded HTML's explicit placement takes precedence over source hints. Conflicting source hints for repeated images are left unresolved instead of guessing. Wide windows place floats beside article blocks; narrow windows stack them in reading order.

Raw source, expanded markup, attributes and embedded styles remain available in the article cache. The renderer applies supported inline styles and simple tag/class/id descendant rules from embedded style sheets. Unsupported containers retain their readable children; unsupported media and widgets receive a Web view notice. It does not run scripts, fetch site-wide stylesheets or implement browser CSS layout. Float wrapping operates between paragraphs rather than individual text lines. Conditional/dynamic CSS, absolute positioning, flex/grid and interactive widgets can differ; use the reader button to return to Web view. Rendering failures fall back to Web view, and failures in lazily expanded sections show a readable fallback. Large articles are not yet virtualized.

The portable article-model tests run with `dotnet test WikiViewer.Tests/WikiViewer.Tests.csproj -c Release`. Validate XAML and the native control using the UWP heads on Windows too.

The editor's **Preview renderer** switch selects native or Web preview, initially following the default reader preference. Preview captures the exact unsaved text and expands it on the originating wiki; switching renderers reuses that same result. Draft previews are never written to the published-article cache. Changed text marks a preview as out of date, and late responses for older drafts are discarded. Native preview uses the same article renderer and falls back to Web preview on rendering errors. Web preview retains the server's page head and resources when supplied.

### Building with WikitextParser

The parser fork is maintained at [megabytesme/WikitextParser](https://github.com/megabytesme/WikitextParser), on the `netstandard1.4` branch. Build/package that checkout, then select the resulting local feed without publishing a package:

```powershell
dotnet pack C:\path\to\WikitextParser\WikitextParser\WikitextParser.csproj -c Release -o C:\path\to\parser-feed
MSBuild "1809 UWP\1809 UWP.csproj" /restore /p:Configuration=Release /p:Platform=ARM64 /p:WikitextParserVersion=0.1.0 /p:RestoreAdditionalProjectSources=C:\path\to\parser-feed
```

Use MSBuild from Visual Studio with UWP targets installed. The version property defaults to the existing published `0.0.1` package until `0.1.0` is published. Selecting the expanded fork enables its document traversal for raw-source layout hints; the published version retains a compatible traversal path. The expanded parser adds local syntax coverage and compatibility diagnostics; it does not implement the entire MediaWiki runtime. Server expansion remains necessary for full wiki-specific content.

## Download

<a href="https://apps.microsoft.com/detail/9NXGG8M4XF48"><img src="https://get.microsoft.com/images/en-us%20dark.svg" width="200"/>

## Build Guide

### Prerequisites

* Windows 11 or later
* Visual Studio 2022 or later
* **Workloads:** Universal Windows Platform development

### Installation

1. Clone the repository:

```sh
   git clone https://github.com/megabytesme/WikiViewer.git
   ```

2. Open the `WikiViewer.sln` solution file in Visual Studio.
3. Restore NuGet packages if prompted.

### Running the Application

1. In the Solution Explorer, right-click the desired project (`1809 UWP` is recommended) and select **"Set as Startup Project"**.
2. Press `F5` or select `Debug > Start Debugging` to build and run.

## Architectural Overview

This project utilizes a **Shared UI Logic (MVVM-like)** architecture to support multiple Windows versions from a single, reusable codebase. This design separates core logic, UI logic, and platform-specific views.

* **`WikiViewer.Core` (Model/Services):** Contains all platform-agnostic business logic, data models and API services. It has no dependency on any UI framework and as a result is highly reusable.
* **`WikiViewer.Shared.Uwp` (Shared UI Logic):** Contains abstract `PageBase` classes that act as a combination of View-Model and Controller. They define the UI state, event handling, and a contract of abstract properties that the concrete Views must implement.
* **`1507 UWP`:** The UWP head for Windows 10 Version 1507 and newer. It implements the UI using the original `WebView` (EdgeHTML) and legacy controls for maximum compatibility on older platforms. Recommended for Windows 10 Mobile users.
* **`1809 UWP`:** The UWP head for Windows 10 Version 1809 and newer. It implements the UI using **WinUI 2**, including the modern `WebView2` (Chromium) for the best performance and user experience. Recommended for most users.
* **`App Assets`:** Holds shared assets used for store listings and branding.

## Contributing

1. Fork the repository.
2. Create a new branch (`git checkout -b feature-branch`).
3. Commit your changes (`git commit -m 'Add new feature'`).
4. Push to the branch (`git push origin feature-branch`).
5. Create a new Pull Request.

## License

This project is licensed under the CC BY-NC-SA 4.0 License - see the [LICENSE](LICENSE.md) file for details.
