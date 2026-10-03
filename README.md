# The Fourth Seal launcher

A small Windows program for players (`dist/TheFourthSealLauncher.exe`).
They press **Play** and it does the following:

1. Finds their Minecraft (the official launcher's `.minecraft`).
2. Downloads the server's version of **All the Mods 9 - To the Sky** from
   CurseForge.
3. Installs the Forge version the pack needs. It uses the Java the
   Minecraft Launcher already has, or Mojang's own Java if it has none.
4. Puts the mods, configs and scripts in its own game folder,
   `%APPDATA%\TheFourthSeal\game`, so the player's other Minecraft stays
   as it was. When the server moves to a new pack version, the next Play
   removes the old mods and copies the new configs. Mods a player added
   themselves are never touched.
5. Saves **The Fourth Seal Skyblock** at the top of the multiplayer server
   list. It also adds a tiny KubeJS client script that opens the server
   list the first time the title screen shows.
6. Adds a "The Fourth Seal Skyblock" profile, with the seal icon and
   enough memory, to the official Minecraft Launcher and opens it. The
   player presses Play there.

Microsoft sign-in stays with the official launcher, so the launcher
never sees anyone's account.

Some mod authors only allow downloads from the CurseForge website. If the
pack has any of those, the launcher lists them with links. The player
downloads each one in their browser, presses **Check again**, and the
launcher picks them up from their Downloads folder.

## Setting it up (server owner, once)

Open `src/Settings.cs` in Notepad and fill in the top section:

- `ServerAddress`: what players type to join, e.g. `play.example.com` or
  `123.45.67.89:25565`.
- `PackFileId`: the server's exact ATM9-TTS version. On CurseForge, open
  the pack's Files tab, click the version your server runs (the
  **client** file, not "Server Files"). The number at the end of that
  page's address is the id (`.../all-the-mods-9-to-the-sky/files/1234567`).
- **The CurseForge API key** goes in `src/ApiKey.cs`, not `Settings.cs`
  (`build.cmd` makes that file from `ApiKey.cs.example` the first time).
  That file is never uploaded anywhere (`.gitignore`), because
  CurseForge's terms say the key must not be shared. Your console key
  (<https://console.curseforge.com/>) only works for Minecraft once
  CurseForge approves your application at
  <https://forms.monday.com/forms/dce5ccb7afda9a1c21dab1a1aa1d84eb?r=use1>.
  Until then it gets "403 Forbidden".
- `ConfigUrl` (optional): the address of a small text file you host, e.g.
  on GitHub, holding
  `{ "serverAddress": "...", "packFileId": 1234567, "message": "..." }`.
  The launcher reads it each time it starts, so after a pack update you
  change that file instead of sending everyone a new .exe.

Then double-click `build.cmd`. It uses the C# compiler built into
Windows, so there's nothing to install. Share
`dist\TheFourthSealLauncher.exe` with players.

## Notes

- **Windows SmartScreen** will warn about an unknown publisher the first
  time, because the .exe isn't code-signed (a signing certificate costs
  money each year). Players click **More info -> Run anyway**.
- Everything the launcher does is written to
  `%APPDATA%\TheFourthSeal\launcher.log`. Ask players for that file when
  something goes wrong.
- Before changing the profile list, the launcher asks the player to close
  the Minecraft Launcher, because it rewrites that list when it closes.
  It keeps one backup: `launcher_profiles.json.fourthseal-backup`.

## Testing

`node tools/launcher/test/run-test.js` runs the real .exe against a fake
CurseForge on this PC, in a scratch folder. It really installs Forge and
Java there. It never touches your own `.minecraft` and never opens the
Minecraft Launcher. Add `FRESH=1` to start from nothing. Rebuild
(`build.cmd`) before testing.
