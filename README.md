# Spotify Album Cover Plugin

Displays the album art of the currently playing Spotify track on a Macro Deck button.

## Features

- Uses Spotify search to find the current track cover
- Updates button icons automatically on track changes
- Keeps the icon pack small by overwriting per-button images

## Requirements

- Macro Deck 2 (target version: 2.14.1)
- Spotify API client ID and client secret

## Install

1. Download the latest `SpotifyAlbumCoverPlugin.macroDeckPlugin` from Releases.
2. In Macro Deck, open Extensions and install the plugin.

## Download

Releases: <https://github.com/felpower/Macro-Deck-Spotify-Album-Cover-Plugin/releases>

Each release includes:

- `SpotifyAlbumCoverPlugin.macroDeckPlugin` for easy install
- Versioned release notes
## Setup

1. Open the plugin configuration and enter your Spotify API Client ID and Client Secret.
2. Add the action `spotify_image` to a button.
3. In the action settings, keep the defaults:
   - Title: `{current_playing_title}`
   - Artist: `{current_playing_artist}`

## Notes

- The plugin uses the official Spotify Web API.
- Cover updates are rate-limited to avoid API throttling.

## Support

Discord: [felpower](https://discord.com/users/232548324614340610)

## License

MIT
