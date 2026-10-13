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
