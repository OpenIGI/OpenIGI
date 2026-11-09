# OpenIGI dedicated server

A headless server for OpenIGI multiplayer: desktop players join it by address on its UDP port, browser players through
its WebRTC room, and it lists itself in the game's server browser. It plays your own copy of the game: it never contains
the game's data, so point it at your installation's folder.

## Quick start

| Host | How |
| --- | --- |
| Linux (systemd) | `tar xzf openigi-server-*-linux-x64.tar.gz && cd openigi-server-* && sudo ./install.sh` |
| Windows | unzip, then in an administrator PowerShell: `.\install.ps1` |
| Docker | see `docker-compose.yml`: game at `./game`, settings in `./data/server.cfg`, `docker compose up -d` |
| By hand | `./OpenIGI.Server -config server.cfg -game /path/to/game -title igi1` |

The installers ask for the game's folder, the server's name, its port, a remote console password and whether to list it.

## Settings

`server.cfg.example` lists every setting with a comment. The ones most servers change:

- `name`, `maxplayers`, `password`, `motd`, `region`
- `rcon_password`: lets an admin run console commands from inside the game (`rcon_password <pw>`, then `rcon status`)
- `public`: list on the master server; `room`: also accept browser players
- `map <mission> <mode>` lines: the rotation. Modes: `coop`, `objective`, `tdm`, `ffa`, `elim`, `sd`
- `mp_buytime <seconds>`: how long players may buy in a search and destroy round (20; 0 for none). Also a console command.
- `mp_moneystart`, `mp_moneycap`, `mp_moneykill`, `mp_moneyteamkill`, `mp_moneywin`, `mp_moneylose`, `mp_moneyplant`: the
  round modes' money -- start money (1000), the most a player holds (15000), a kill's pay (800), a team kill's fine (4000), a
  round's pay to its winners (1250) and losers (750), the bomb planter's pay (300). Also console commands; they take hold at once.

The command line wins over the file: `-servername`, `-port`, `-maxplayers`, `-password`, `-mode`, `-mission`.

## Console

Type at the server's terminal (`docker attach openigi-server` under Docker), or use `rcon <command>` in game:

```
status                          the server, its map and its players (#slot, name, team, score, ping, key)
say <text>                      tell every player
kick <player> [reason]          put a player out
ban <player> [minutes] [reason] put a player out and keep its key out (0 or none: for ever)
unban <name|id>                 lift a ban
bans                            the bans in force
map <mission> [mode]            go on to a mission now
next                            go on to the rotation's next map
mode <mode>                     play a mode from the next map
rotation                        the maps played in turn
quit                            stop the server
```

Players are named by their name, an unambiguous start of it, or `#slot` from `status`. Bans follow the player's key,
not its name, and are kept in `bans.json`.

## Ports

- UDP `26001` (or your `port`): desktop players. Open it in your firewall and your provider's.
- Browser players need no open port: they reach the server through its room, relayed when they must be.
