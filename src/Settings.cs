// The launcher's settings. The server owner fills these in before
// building (see tools/launcher/README.md). If ConfigUrl is set, the
// launcher also reads a small JSON file from there on every start, so the
// address or pack version can change without sending players a new .exe:
//   { "serverName": "...", "serverAddress": "...", "packFileId": 1234567,
//     "packVersion": "1.1.3", "message": "Shown in the launcher window" }
using System;
using System.IO;

namespace FourthSealLauncher
{
    static class Settings
    {
        // ---- Fill these in ----
        public static string ServerName = "The Fourth Seal Skyblock";
        public static string ServerAddress = "144.217.35.156";
        // The server's ATM9-TTS version: the CurseForge file id of the
        // CLIENT pack file (the number at the end of its page address,
        // curseforge.com/minecraft/modpacks/all-the-mods-9-to-the-sky/files/<id>).
        public static long PackFileId = 7976938; // atm9sky-1.1.9 (client pack, April 2026)
        // Used only if PackFileId is 0: the newest pack file whose name
        // contains this text (e.g. "1.1.3"). Empty = the newest file.
        public static string PackVersion = "1.1.9";
        public static string CurseForgeApiKey = ApiKey.Value; // src/ApiKey.cs
        // The owner's secret Gist (edit it at
        // https://gist.github.com/EarlGreyFae/0d18b15750e8cf6a5a805dfab8791167).
        // This link always serves the latest edit (GitHub may cache it for a
        // few minutes).
        public static string ConfigUrl = "https://gist.githubusercontent.com/EarlGreyFae/0d18b15750e8cf6a5a805dfab8791167/raw/fourthseal-launcher.json";
        // ---- Usually left alone ----
        public static string PackSlug = "all-the-mods-9-to-the-sky";
        public static string CurseForgeApi = "https://api.curseforge.com";
        public static string ProfileId = "fourthseal";
        public static string Message = "";

        // Where the player's Minecraft lives, and where this launcher keeps
        // the game (mods, configs, worlds) and its downloads.
        public static string MinecraftDir;
        public static string HomeDir;
        public static string GameDir { get { return Path.Combine(HomeDir, "game"); } }
        public static string CacheDir { get { return Path.Combine(HomeDir, "cache"); } }
        // Testing only: don't open the Minecraft Launcher at the end.
        public static bool NoLaunch;

        public static void Load()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            MinecraftDir = Path.Combine(appData, ".minecraft");
            HomeDir = Path.Combine(appData, "TheFourthSeal");
            // Overrides for testing (never needed by players).
            MinecraftDir = Env("FS_MINECRAFT_DIR", MinecraftDir);
            HomeDir = Env("FS_HOME", HomeDir);
            CurseForgeApi = Env("FS_API_BASE", CurseForgeApi);
            CurseForgeApiKey = Env("FS_API_KEY", CurseForgeApiKey);
            ServerAddress = Env("FS_SERVER_ADDRESS", ServerAddress);
            ConfigUrl = Env("FS_CONFIG_URL", ConfigUrl);
            string fileId = Env("FS_PACK_FILE_ID", null);
            if (fileId != null) long.TryParse(fileId, out PackFileId);
            PackVersion = Env("FS_PACK_VERSION", PackVersion);
            NoLaunch = Env("FS_NO_LAUNCH", null) != null;
        }

        // The hosted settings file, if there is one. Problems are ignored:
        // the built-in settings still work.
        public static string LoadRemote()
        {
            if (string.IsNullOrEmpty(ConfigUrl) || ConfigUrl == "none") return null;
            try
            {
                var o = Json.Parse(Net.GetText(ConfigUrl, null));
                if (!string.IsNullOrEmpty(Json.Str(o, "serverName"))) ServerName = Json.Str(o, "serverName");
                if (!string.IsNullOrEmpty(Json.Str(o, "serverAddress"))) ServerAddress = Json.Str(o, "serverAddress");
                if (Json.Long(o, "packFileId") > 0) PackFileId = Json.Long(o, "packFileId");
                if (Json.Str(o, "packVersion") != null) PackVersion = Json.Str(o, "packVersion");
                if (Json.Str(o, "message") != null) Message = Json.Str(o, "message");
                return null;
            }
            catch (Exception e)
            {
                return "Couldn't read the online settings (" + e.Message + ") - using the built-in ones.";
            }
        }

        static string Env(string name, string fallback)
        {
            var v = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(v) ? fallback : v;
        }
    }
}
