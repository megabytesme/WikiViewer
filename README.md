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

Configure **Settings > Connection > Resolver URL / username / password** to enable the optional Proxy connection method. The URL can be the resolver base or its `/v1/request` endpoint. Proxy uses HTTPS POST with per-request HTTP Basic authentication. Credentials are stored in Windows Credential Locker; ordinary settings contain only the endpoint and a vault reference. A blank password keeps the saved password only for the same URL and username. **Remove resolver** deletes the saved configuration and disables Proxy. **Import credentials from JSON** accepts `Username`/`Password` at the root or inside a `Resolver` object; select Save resolver after importing. Auto detection skips Proxy when it is unconfigured. WebView navigation/initialization now has timeouts and failed workers are discarded so a stalled challenge cannot wait indefinitely or poison the pool.

The article command bar shows **Page** with a page icon in native mode and **Website** with a website icon in Web mode; selecting it switches modes. Enable **Settings > Reading > Use native wikitext reader by default** to open articles in native mode automatically. The saved preference applies in both UWP heads and persists across app restarts; the article's reader button remains a temporary override. Web view is the initial default until the preference is enabled. Both UWP heads fetch raw wikitext, server-expanded HTML and the revision ID in one `action=parse` response, and cache them together. Templates, parser functions, Lua modules and wiki extensions are expanded by the wiki's server. Native controls render the expanded content, supplemented by placement and sizing hints parsed from the raw source. This is a combined source pipeline; it does not evaluate templates locally.

Native content colours follow the app theme. Dark mode mutes off-white panels and pastel headers while retaining their colour hints; text and links are checked against the effective background for readability. Nested translucent backgrounds are composited, and high contrast uses system colours. The modern reader updates its brushes when the theme changes without reloading the article. Images and media retain their original colours. Touch/pen taps activate inline links, including text overflow regions, while keeping text selection available. Block and floated links retain their navigation targets.

The reader supports paragraphs, headings, text formatting, internal/external links, citation anchors, images, nested lists and scrollable tables with spans. It preserves source order, captions, infoboxes, thumbnail placement, declared dimensions, alignment, colours, borders, spacing, columns and collapsible sections where native controls can express them. Expanded HTML's explicit placement takes precedence over source hints. Conflicting source hints for repeated images are left unresolved instead of guessing. Wide windows place floats beside article blocks; narrow windows stack them in reading order.

Raw source, expanded markup, attributes and resolved styles remain available in the article cache. The reader fetches a bounded set of same-origin stylesheets and ResourceLoader style modules returned by the server. It supports static descendant/child/sibling selectors, attributes, `:not`, structural pseudo-classes, width media queries at render time, inherited CSS variables and `!important`. Selectors and declarations compile once per document. Dynamic selectors, imports and unsupported conditions are skipped. Galleries and simple equal-track grids/flex groups use responsive native rows; this is a subset of CSS layout.

The modern head renders static SVG, including the wiki's SVG math fallback images. Inline SVG retains static shapes and removes active elements/external references. The legacy head provides a readable Web fallback for vectors. Audio/video preserve source choices, posters and surrounding captions/fallback text, and play through Windows media controls when requested. External timed subtitle tracks are not yet connected to native playback. Codec availability depends on Windows; another source, the media-file link or Web view remains available. Interactive maps, charts and JavaScript widgets keep using Web view.

Both heads render common presentation MathML constructs: fractions, roots, scripts, limits, fences and matrices. Native math font metrics approximate browser typesetting. Relative image containers can place absolute markers/labels using offsets, percentages, translations and stacking order. Ordinary paragraphs flow through native text-overflow regions beside floats and return to full width below them.

Large pages with section headings use a virtualized section list, releasing controls and media when sections are recycled; individual large tables/sections still render as a unit. Article and draft model parsing runs off the UI thread, and obsolete results are discarded. Links to unrealized anchors bring their section into view.

Saved articles appear before connection detection, revision checks, permissions or optional styles are fetched. Repeat visits reuse bounded article/model memory caches; syntax and same-origin styles share a persistent cache across pages. Revision checks run in the background at most once per five minutes per article, with periodic server expansion to pick up template changes. A newer saved revision is offered through **Reload page**, preserving your current reading position. Late styles keep existing text visible during preparation and apply automatically only before interaction; otherwise Reload applies them. Web mode remains available with the classic custom-theme HTML renderer.

Images load independently with a delayed spinner, cached bytes are reused, and queued requests for recycled sections are cancelled. Audio/video display a loading bar when requested and use Windows buffering progress where available. Favourites and download caching share article requests with the reader, including identical titles on different wikis. Local diagnostic timings retain only a bounded list of phases/durations in `article-load-metrics.json`, without titles or credentials.

Wiki `siteinfo` supplies localized file/category/redirect aliases, image-option aliases and safely convertible link trails. This configuration is cached with each article. Unsupported PCRE link-trail syntax uses an explicit English fallback. Parser 0.2.0 also supports balanced inclusion controls while retaining original source. Templates, Lua, signatures, substitution and extension semantics still use server expansion.

Unsupported containers retain their readable children, and unsupported content receives a Web view notice. Arbitrary browser positioning, complex flex/grid, live responsive CSS, uncommon MathML, malformed/ambiguous wikitext recovery and exact browser typography remain limits. Rendering failures fall back to Web view, and failures in lazily expanded or virtualized sections show a readable fallback. The compatibility suite and live parser comparison tool establish targeted coverage, not complete MediaWiki conformance.

The portable article-model tests run with `dotnet test WikiViewer.Tests/WikiViewer.Tests.csproj -c Release`. Validate XAML and the native control using the UWP heads on Windows too.

The editor's **Preview renderer** switch selects native or Web preview, initially following the default reader preference. Preview captures the exact unsaved text and expands it on the originating wiki; switching renderers reuses that same result. Draft previews are never written to the published-article cache. Changed text marks a preview as out of date, and late responses for older drafts are discarded. Native preview uses the same article renderer and falls back to Web preview on rendering errors. Web preview retains the server's page head and resources when supplied.

### Building with WikitextParser

The parser fork is maintained at [megabytesme/WikitextParser](https://github.com/megabytesme/WikitextParser), on the `netstandard1.4` branch. Build/package that checkout, then select the resulting local feed without publishing a package:

```powershell
dotnet pack C:\path\to\WikitextParser\WikitextParser\WikitextParser.csproj -c Release -o C:\path\to\parser-feed
MSBuild "1809 UWP\1809 UWP.csproj" /restore /p:Configuration=Release /p:Platform=ARM64 /p:WikitextParserVersion=0.2.0 /p:RestoreAdditionalProjectSources=C:\path\to\parser-feed
```

Use MSBuild from Visual Studio with UWP targets installed. The version property defaults to the existing published `0.0.1` package until the expanded package is published. Selecting 0.2.0 enables localized raw-source parsing; 0.1.0 retains the earlier traversal path; the published version retains a compatible traversal path. The expanded parser adds local syntax coverage and compatibility diagnostics; it does not implement the entire MediaWiki runtime. Server expansion remains necessary for full wiki-specific content.

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
