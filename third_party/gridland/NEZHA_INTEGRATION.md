# Gridland in the Nezha desktop pet

Upstream: https://github.com/doublespeakgames/gridland
Upstream commit: 06af02c3af7062aead04a1e6f67b6e59f3e4b6a1
Original code, game design and graphics: Michael Townsend / doublespeak games.
Original music: Vince Nitro; desktop entry currently starts silent.
Original license: MPL-2.0, retained in LICENSE. Original source is included here.

Changes made on 2026-10-13: offline resource references and analytics disabled;
Chinese menu; embedded desktop pause and session bridge; rule snapshots for
the board, growth, day/night position, actors, health and remaining status effects.
The new bridge files are desktop integration code. Modified original modules
remain under their original MPL-2.0 license.

Session restore rebuilds transient actor animations and movement decisions.
A resource being carried is returned to the saved store. Temporary projectiles
are not serialized. This is not a deterministic replay of animation frames.

Native growth import/export does not include the desktop board/battle snapshot.
Copying the whole Nezha data directory is the supported desktop save transfer.

2026-10-13: declare UTF-8 in the offline HTML document and desktop stylesheet.
This fixes Chinese CSS-generated saving text being decoded as Windows-1252.
The game rules, session format and original attribution remain unchanged.

2026-10-13: desktop death feedback pauses on a Chinese retry dialog instead of
silently restarting. The world still counts the death and retains the upstream
growth checkpoint. Continue reloads the local page and starts in daylight as
before; old animation callbacks cannot change the new board. The native host
suppresses captures during this navigation and injects current pause/session data.
A local browser flag preserves the pending dialog when the window is reopened;
the defeated battle remains ended. Capturing during death cannot save zero HP
over the retained growth checkpoint. Browser-only upstream behavior is retained.
