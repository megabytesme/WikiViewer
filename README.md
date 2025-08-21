# WikiViewer

<img width="1919" height="1079" alt="image" src="https://github.com/user-attachments/assets/01570b60-d75d-45c0-87bb-6c69fa414550" />

**Wiki Viewer** is an unofficial, third-party client for browsing any MediaWiki-powered site - from Wikipedia, to Fandom and beyond - without your web browser. It provides a clean, native, and performance-tuned experience for Windows, both online and offline.

**Disclaimer:** This is an unofficial, third-party client for browsing MediaWiki sites. This app was created by [MegaBytesMe](https://github.com/megabytesme) and is not affiliated with, endorsed, or sponsored by the operators of any specific wiki. All article data, content, and trademarks are the property of their respective owners and contributors. This app simply provides a native viewing experience for publicly available content.

## Features

- **Universal Wiki Support:** Connect to your favorite MediaWiki site by simply changing the URL in the settings. From Wikipedia to niche wikis, enjoy them all in one app.

- **Clean & Native Interface:** Experience a fast, fluid, and ad-free reading experience designed for Windows, free from the clutter of a standard web browser.

- **Advanced Offline Caching:** Save articles and all their images directly to your device. The intelligent caching system ensures you can continue reading even when you're without an internet connection.

- **Full Account Integration:** Sign in with your wiki account to edit pages, manage your watchlist, and contribute to the community directly from the app, with support for two-factor authentication (2FA).

- **Favourites & Watchlist Sync:** Keep a list of your most-visited articles. When logged in, this list automatically syncs with your official wiki watchlist.

- **Performance Tuning:** Take control of system resources with a unique slider to manage concurrent downloads, balancing speed against RAM and CPU usage to fit your hardware.

- **Smart Search with Suggestions:** Find articles quickly with a built-in search bar that provides instant suggestions as you type, powered directly by the MediaWiki API.

- **Discover Something New:** Jump to a random article with a single click and explore the depths of your favorite topics.

- **Privacy-Focused by Design:** Your data is your own. The app collects no personal information, and all caches, favourites, and credentials are stored securely and only on your local device.

## Download

<a href="https://apps.microsoft.com/detail/9NXGG8M4XF48"><img src="https://get.microsoft.com/images/en-us%20dark.svg" width="200"/>

## Build Guide

### Prerequisites

- Windows 11 or later
- Visual Studio 2022 or later
- **Workloads:** Universal Windows Platform development

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

This project utilizes a **Shared UI Logic (MVC)** architecture to support multiple Windows 10/11 versions from a single, highly reusable codebase.

- **`Shared Project (UWP)` (Model/Services):** Contains all platform-agnostic business logic, data models, API services, and caching mechanisms.
- **`WikiViewer.Shared.Uwp` (Shared UI Logic):** Contains abstract `PageBase` classes that act as Controllers/ViewModels. They define the UI state, event handling, and a contract of abstract properties that the Views must implement.
- **`1703 UWP` (Legacy View):** The UWP head for Windows 10 Version 1703 and newer. It implements the UI using the original `WebView` (EdgeHTML) and legacy controls for maximum compatibility. Recommended for Windows 10 Mobile users.
- **`1809 UWP` (Modern View):** The UWP head for Windows 10 Version 1809 and newer. It implements the UI using **WinUI 2**, including the modern `WebView2` (Chromium) and `NavigationView` for the best performance and user experience. Recommended for most users.
- **`App Assets`:** Holds shared assets used for store listings and branding.

## Contributing

1. Fork the repository.
2. Create a new branch (`git checkout -b feature-branch`).
3. Commit your changes (`git commit -m 'Add new feature'`).
4. Push to the branch (`git push origin feature-branch`).
5. Create a new Pull Request.

## License

This project is licensed under the CC BY-NC-SA 4.0 License - see the [LICENSE](LICENSE.md) file for details.
