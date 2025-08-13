# Wikibeta

WikiBeta is an unofficial, third-party client for browsing the BetaWiki (not affiliated) without your web browser, online and offline (after caching).

## Features

- **Clean & Native Interface:** Enjoy a fast, fluid, and ad-free reading experience designed for Windows, free from the clutter of a standard web browser.

- **Offline Reading & Caching:** Save your favourite articles and all their images directly to your device, allowing you to continue reading even when you're without an internet connection.

- **Full Account Integration:** Sign in with your BetaWiki account to edit pages, manage your watchlist, and contribute to the community directly from the app.

- **Favourites & Watchlist Sync:** Keep a list of your most-visited articles. When you're logged in, this list automatically syncs with your official BetaWiki watchlist.

- **Advanced Performance Tuning:** Take control of your system's resources with a unique slider to manage concurrent downloads, balancing speed against RAM and CPU usage to fit your hardware.

- **Smart Search with Suggestions:** Find articles quickly with a built-in search bar that provides instant suggestions as you type, powered directly by the MediaWiki API.

- **Discover Something New:** Jump to a random article with a single click and explore the depths of software history.

- **Privacy-Focused Design:** Your data is your own. WikiBeta collects no personal information, and all your caches, favourites, and credentials are stored securely and only on your local device.

## Download

<a href="https://apps.microsoft.com/detail/9N5V233G177B"><img src="https://get.microsoft.com/images/en-us%20dark.svg" width="200"/>

## Build Guide

### Prerequisites

- Windows 11 or later
- Visual Studio 2022 or later

### Installation

1. Clone the repository:
   ```sh
   git clone https://github.com/megabytesme/WikiBeta.git
   ```
2. Open the project in Visual Studio.
3. Build the solution.

### Running the Application

1. Start the application by pressing `F5` or by selecting `Debug > Start Debugging`.

## Folder Structure

- `1809 UWP`: UWP app implementation which supports devices on Windows 1809 and above (Includes 10X!) - Uses WinUI 2. Recommended for all Windows device users.
- `App Assets`: Folder which holds the assets used by all projects when publishing to App Stores.

## Contributing

1. Fork the repository.
2. Create a new branch (`git checkout -b feature-branch`).
3. Commit your changes (`git commit -m 'Add new feature'`).
4. Push to the branch (`git push origin feature-branch`).
5. Create a new Pull Request.

## License

This project is licensed under the CC BY-NC-SA 4.0 License - see the [LICENSE](LICENSE.md) file for details.
