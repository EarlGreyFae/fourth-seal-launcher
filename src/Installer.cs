// Everything the launcher does, in order:
//   1. find the player's Minecraft (the official launcher's .minecraft)
//   2. find the server's ATM9-TTS version on CurseForge and download it
//   3. install the Forge version that pack needs (headless Forge installer,
//      run with the Minecraft launcher's Java, or Mojang's own Java
//      downloaded here if there's none)
//   4. download the pack's mods into the launcher's own game folder
//      (mods that don't allow outside downloads: the player downloads them
//      from CurseForge in their browser and the launcher picks them up)
//   5. copy the pack's configs (only when the pack version changes)
//   6. save The Fourth Seal to the multiplayer server list, and add the
//      small script that opens the server list when the game starts
//   7. add a "The Fourth Seal Skyblock" profile to the Minecraft Launcher
//      and open it, so the player just presses Play
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FourthSealLauncher
{
    class ManualFile
    {
        public string Name;      // the mod's name
        public string FileName;
        public string PageUrl;   // where the player downloads it
        public string Target;    // where it goes
        public string Sha1;
        public long Size;
    }

    class Installer
    {
        public Action<string> Log = delegate { };
        public Action<string> Status = delegate { };
        public Action<int, int> Progress = delegate { };
        // The Minecraft Launcher is open: true = check again, false = give up.
        public Func<bool> AskCloseLauncher = delegate { return false; };
        // Mods to download by hand: true = check again, false = give up.
        public Func<List<ManualFile>, bool> AskManual = delegate { return false; };

        public string VersionId; // the Forge version the profile uses (after Run)

        const string StateFile = "state.json";
        static readonly string[] KeepIfPresent = { "options.txt", "servers.dat", "optionsof.txt", "optionsshaders.txt" };

        public void Run()
        {
            Net.Init();
            var warning = Settings.LoadRemote();
            if (warning != null) Log(warning);
            if (!string.IsNullOrEmpty(Settings.Message)) Log(Settings.Message);
            CheckSettings();

            Status("Finding Minecraft...");
            FindMinecraft();
            WaitForLauncherClosed();
            Directory.CreateDirectory(Settings.GameDir);
            Directory.CreateDirectory(Settings.CacheDir);

            var state = LoadState();
            var cf = new CurseForge(Settings.CurseForgeApi, Settings.CurseForgeApiKey);

            Status("Checking the server's modpack version...");
            var packFile = ResolvePack(cf);
            long packFileId = Json.Long(packFile, "id");
            Log("Modpack: " + Json.Str(packFile, "displayName") + " (file " + packFileId + ")");
            string zip = DownloadPack(packFile);
            var manifest = ReadManifest(zip);

            VersionId = InstallForge(manifest);

            var overrideMods = Json.List(Json.Get(state, "overrideMods")).Select(o => Convert.ToString(o)).ToList();
            if (Json.Long(state, "packFileId") != packFileId || !Directory.Exists(Path.Combine(Settings.GameDir, "config")))
            {
                Status("Copying the modpack's settings...");
                overrideMods = ApplyOverrides(zip, manifest);
            }

            var modFiles = SyncMods(cf, manifest, state, overrideMods);

            Status("Saving The Fourth Seal to your server list...");
            WriteClientScript();
            if (ServerList.EnsureServer(Path.Combine(Settings.GameDir, "servers.dat"), Settings.ServerName, Settings.ServerAddress))
                Log("Added " + Settings.ServerName + " (" + Settings.ServerAddress + ") to the top of the multiplayer list.");

            UpdateProfiles(VersionId);

            var newState = new Dictionary<string, object>();
            newState["packFileId"] = packFileId;
            newState["files"] = modFiles.ToArray();
            newState["overrideMods"] = overrideMods.ToArray();
            newState["versionId"] = VersionId;
            File.WriteAllText(Path.Combine(Settings.HomeDir, StateFile), Json.Write(newState));

            Status("Ready!");
            if (!Settings.NoLaunch) OpenMinecraftLauncher();
        }

        void CheckSettings()
        {
            if (string.IsNullOrEmpty(Settings.ServerAddress) || Settings.ServerAddress.IndexOf("HERE", StringComparison.Ordinal) >= 0)
                throw new LauncherException("This launcher hasn't been set up yet: the server address is missing (Settings.cs).");
            if (string.IsNullOrEmpty(Settings.CurseForgeApiKey))
                throw new LauncherException("This launcher hasn't been set up yet: the CurseForge API key is missing (Settings.cs).");
        }

        // ---- 1. Minecraft ----

        void FindMinecraft()
        {
            string dir = Settings.MinecraftDir;
            if (!Directory.Exists(dir))
                throw new LauncherException("Couldn't find Minecraft on this PC (no " + dir + "). Install the Minecraft Launcher from minecraft.net, sign in, run it once, then try again.");
            if (!File.Exists(Path.Combine(dir, "launcher_profiles.json")) && !File.Exists(Path.Combine(dir, "launcher_profiles_microsoft_store.json")))
            {
                // The Forge installer needs this file; the launcher fills it in.
                File.WriteAllText(Path.Combine(dir, "launcher_profiles.json"), "{\"profiles\":{},\"settings\":{},\"version\":3}");
                Log("Made an empty launcher_profiles.json (the Minecraft Launcher fills it in).");
            }
            Log("Minecraft found: " + dir);
        }

        static readonly string[] LauncherProcesses = { "MinecraftLauncher", "Minecraft" };

        static bool LauncherRunning()
        {
            foreach (var name in LauncherProcesses)
            {
                var found = Process.GetProcessesByName(name);
                bool any = found.Length > 0;
                foreach (var p in found) p.Dispose();
                if (any) return true;
            }
            return false;
        }

        // The Minecraft Launcher rewrites its profile list when it closes,
        // which would undo the new profile - so it has to be closed first.
        void WaitForLauncherClosed()
        {
            if (Settings.NoLaunch) return;
            while (LauncherRunning())
            {
                Status("Waiting for the Minecraft Launcher to close...");
                if (!AskCloseLauncher()) throw new LauncherException("Stopped: the Minecraft Launcher was still open.");
            }
        }

        // ---- 2. The modpack ----

        object ResolvePack(CurseForge cf)
        {
            long packId = cf.FindModpackId(Settings.PackSlug);
            if (Settings.PackFileId > 0)
            {
                var file = cf.GetFile(packId, Settings.PackFileId);
                if (Json.Bool(file, "isServerPack", false))
                    throw new LauncherException("The pack file set in the launcher (" + Settings.PackFileId + ") is the SERVER pack - it needs the client pack file's id.");
                return file;
            }
            var candidates = cf.AllFiles(packId).Where(f => !Json.Bool(f, "isServerPack", false)).ToList();
            if (!string.IsNullOrEmpty(Settings.PackVersion))
                candidates = candidates.Where(f => (Json.Str(f, "displayName") + " " + Json.Str(f, "fileName")).IndexOf(Settings.PackVersion, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (candidates.Count == 0)
                throw new LauncherException("Couldn't find the modpack version '" + Settings.PackVersion + "' on CurseForge.");
            return candidates[0];
        }

        string DownloadPack(object file)
        {
            string path = Path.Combine(Settings.CacheDir, "pack-" + Json.Long(file, "id") + ".zip");
            string sha1 = CurseForge.Sha1Of(file);
            long size = Json.Long(file, "fileLength");
            if (Net.Matches(path, sha1, size)) return path;
            string url = Json.Str(file, "downloadUrl");
            if (string.IsNullOrEmpty(url)) throw new LauncherException("CurseForge won't let the modpack file be downloaded outside its app.");
            Status("Downloading the modpack (" + Megabytes(size) + ")...");
            Net.Download(url, path, sha1, size);
            return path;
        }

        static object ReadManifest(string zip)
        {
            using (var archive = ZipFile.OpenRead(zip))
            {
                var entry = archive.GetEntry("manifest.json");
                if (entry == null) throw new LauncherException("The modpack download has no manifest.json - it may not be a CurseForge client pack.");
                using (var reader = new StreamReader(entry.Open()))
                    return Json.Parse(reader.ReadToEnd());
            }
        }

        // ---- 3. Forge ----

        string InstallForge(object manifest)
        {
            var minecraft = Json.Get(manifest, "minecraft");
            string mcVersion = Json.Str(minecraft, "version");
            string loader = null;
            foreach (var l in Json.Arr(minecraft, "modLoaders"))
                if (loader == null || Json.Bool(l, "primary", false)) loader = Json.Str(l, "id");
            if (loader == null || !loader.StartsWith("forge-"))
                throw new LauncherException("This launcher only knows how to install Forge, and the pack wants '" + loader + "'.");
            string forgeVersion = loader.Substring("forge-".Length);
            string versionId = mcVersion + "-forge-" + forgeVersion;
            string versionJson = Path.Combine(Settings.MinecraftDir, "versions", versionId, versionId + ".json");
            if (File.Exists(versionJson))
            {
                Log("Forge " + forgeVersion + " is already installed.");
                return versionId;
            }

            string java = FindJava() ?? DownloadJava();
            string full = mcVersion + "-" + forgeVersion;
            string url = "https://maven.minecraftforge.net/net/minecraftforge/forge/" + full + "/forge-" + full + "-installer.jar";
            string installer = Path.Combine(Settings.CacheDir, "forge-" + full + "-installer.jar");
            Status("Downloading Forge " + forgeVersion + "...");
            string sha1 = null;
            try { sha1 = Net.GetText(url + ".sha1", null).Trim(); }
            catch (Exception) { sha1 = null; }
            if (!Net.Matches(installer, sha1, 0)) Net.Download(url, installer, sha1, 0);

            Status("Installing Forge " + forgeVersion + " (this takes a minute or two)...");
            int exit = RunProcess(java, "-jar \"" + installer + "\" --installClient \"" + Settings.MinecraftDir + "\"", Settings.CacheDir);
            if (exit != 0 || !File.Exists(versionJson))
                throw new LauncherException("The Forge installer didn't finish (exit code " + exit + "). The details are in the log below.");
            Log("Forge " + forgeVersion + " installed.");
            return versionId;
        }

        int RunProcess(string exe, string args, string workDir)
        {
            var psi = new ProcessStartInfo(exe, args);
            psi.WorkingDirectory = workDir;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (var p = new Process())
            {
                p.StartInfo = psi;
                p.OutputDataReceived += (s, e) => { if (e.Data != null) Log("  " + e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) Log("  " + e.Data); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        // The Java 17 the Minecraft Launcher downloads for 1.20.x, wherever
        // this PC's launcher keeps it, or one we downloaded before.
        string FindJava()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roots = new List<string>
            {
                Path.Combine(Settings.MinecraftDir, "runtime"),
                Path.Combine(local, @"Packages\Microsoft.4297127D64EC6_8wekyb3d8bbwe\LocalCache\Local\runtime"),
                Path.Combine(local, @"Packages\Microsoft.MinecraftJavaEdition_8wekyb3d8bbwe\LocalCache\Local\runtime")
            };
            // Testing only: pretend this PC has no Minecraft Java.
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FS_SKIP_SYSTEM_JAVA"))) roots.Clear();
            foreach (var root in roots)
            {
                foreach (var runtime in new[] { "java-runtime-gamma", "java-runtime-delta" })
                {
                    string java = Path.Combine(root, runtime, "windows-x64", runtime, "bin", "java.exe");
                    if (File.Exists(java)) { Log("Using Minecraft's Java: " + java); return java; }
                }
            }
            string ours = Path.Combine(Settings.HomeDir, "runtime", "java-runtime-gamma", "bin", "java.exe");
            if (File.Exists(ours) && File.Exists(Path.Combine(Settings.HomeDir, "runtime", "java-runtime-gamma", ".complete")))
            {
                Log("Using the launcher's Java: " + ours);
                return ours;
            }
            return null;
        }

        // Mojang's own Java 17 (the same files the Minecraft Launcher uses).
        string DownloadJava()
        {
            Status("Downloading Java for the Forge installer...");
            const string allUrl = "https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";
            var all = Json.Parse(Net.GetText(allUrl, null));
            string platform = Environment.Is64BitOperatingSystem ? "windows-x64" : "windows-x86";
            var runtimes = Json.Arr(Json.Get(all, platform), "java-runtime-gamma");
            if (runtimes.Count == 0) throw new LauncherException("Mojang has no Java download for this PC (" + platform + ").");
            string manifestUrl = Json.Str(Json.Get(runtimes[0], "manifest"), "url");
            var files = Json.Obj(Json.Parse(Net.GetText(manifestUrl, null)), "files");
            string root = Path.Combine(Settings.HomeDir, "runtime", "java-runtime-gamma");
            var jobs = new List<KeyValuePair<string, object>>();
            foreach (var kv in files)
            {
                string type = Json.Str(kv.Value, "type");
                string target = Path.Combine(root, kv.Key.Replace('/', Path.DirectorySeparatorChar));
                if (type == "directory") Directory.CreateDirectory(target);
                else if (type == "file") jobs.Add(new KeyValuePair<string, object>(target, Json.Get(Json.Get(kv.Value, "downloads"), "raw")));
            }
            RunParallel(jobs.Count, i =>
            {
                var raw = jobs[i].Value;
                if (!Net.Matches(jobs[i].Key, Json.Str(raw, "sha1"), Json.Long(raw, "size")))
                    Net.Download(Json.Str(raw, "url"), jobs[i].Key, Json.Str(raw, "sha1"), Json.Long(raw, "size"));
            });
            File.WriteAllText(Path.Combine(root, ".complete"), DateTime.UtcNow.ToString("o"));
            string java = Path.Combine(root, "bin", "java.exe");
            if (!File.Exists(java)) throw new LauncherException("Java downloaded, but java.exe isn't where it should be.");
            Log("Downloaded Java to " + root);
            return java;
        }

        // ---- 4. Mods ----

        static string FolderForClass(long classId)
        {
            if (classId == 12) return "resourcepacks";
            if (classId == 6552) return "shaderpacks";
            return "mods";
        }

        // Returns every file (relative to the game folder) the pack put there.
        List<string> SyncMods(CurseForge cf, object manifest, object state, List<string> overrideMods)
        {
            Status("Checking the mod list...");
            var entries = Json.Arr(manifest, "files").Where(e => Json.Bool(e, "required", true)).ToList();
            var files = cf.GetFiles(entries.Select(e => Json.Long(e, "fileID")));
            var mods = cf.GetMods(entries.Select(e => Json.Long(e, "projectID")));

            var wanted = new List<string>();
            var downloads = new List<KeyValuePair<string, object>>();
            var manual = new List<ManualFile>();
            int index = 0;
            foreach (var e in entries)
            {
                Progress(++index, entries.Count);
                long fileId = Json.Long(e, "fileID");
                long modId = Json.Long(e, "projectID");
                object file;
                if (!files.TryGetValue(fileId, out file))
                {
                    Log("Warning: CurseForge has no file " + fileId + " (mod " + modId + ") any more - skipped.");
                    continue;
                }
                object mod;
                mods.TryGetValue(modId, out mod);
                string rel = FolderForClass(Json.Long(mod, "classId")) + "/" + Json.Str(file, "fileName");
                wanted.Add(rel);
                string target = Path.Combine(Settings.GameDir, rel.Replace('/', Path.DirectorySeparatorChar));
                string sha1 = CurseForge.Sha1Of(file);
                long size = Json.Long(file, "fileLength");
                if (Net.Matches(target, sha1, size)) continue;
                if (!string.IsNullOrEmpty(Json.Str(file, "downloadUrl")))
                {
                    downloads.Add(new KeyValuePair<string, object>(target, file));
                    continue;
                }
                var mf = new ManualFile();
                mf.Name = Json.Str(mod, "name") ?? Json.Str(file, "displayName");
                mf.FileName = Json.Str(file, "fileName");
                string site = Json.Str(Json.Get(mod, "links"), "websiteUrl");
                mf.PageUrl = string.IsNullOrEmpty(site) ? "https://www.curseforge.com/projects/" + modId : site.TrimEnd('/') + "/files/" + fileId;
                mf.Target = target;
                mf.Sha1 = sha1;
                mf.Size = size;
                manual.Add(mf);
            }

            if (downloads.Count > 0)
            {
                Status("Downloading " + downloads.Count + " mods (" + Megabytes(downloads.Sum(d => Json.Long(d.Value, "fileLength"))) + ")...");
                RunParallel(downloads.Count, i =>
                {
                    var f = downloads[i].Value;
                    Net.Download(Json.Str(f, "downloadUrl"), downloads[i].Key, CurseForge.Sha1Of(f), Json.Long(f, "fileLength"));
                });
                Log("Downloaded " + downloads.Count + " mod files.");
            }
            if (manual.Count > 0) GetManualFiles(manual);

            // Mods from the old pack version that the new one dropped. Only
            // files this launcher put there are touched - anything the player
            // added themselves stays.
            var now = new HashSet<string>(wanted.Concat(overrideMods), StringComparer.OrdinalIgnoreCase);
            foreach (var old in Json.List(Json.Get(state, "files")).Select(o => Convert.ToString(o)))
            {
                if (now.Contains(old)) continue;
                string path = Path.Combine(Settings.GameDir, old.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path)) { File.Delete(path); Log("Removed " + old + " (no longer in the pack)."); }
            }
            Progress(0, 0);
            return now.ToList();
        }

        // Files CurseForge only lets people download through its website:
        // the player downloads them in their browser, and they're picked up
        // from their Downloads folder.
        void GetManualFiles(List<ManualFile> manual)
        {
            string downloadsDir = Environment.GetEnvironmentVariable("FS_DOWNLOADS_DIR");
            if (string.IsNullOrEmpty(downloadsDir))
                downloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            while (true)
            {
                foreach (var m in manual.ToList())
                {
                    string found = Path.Combine(downloadsDir, m.FileName);
                    if (Net.Matches(m.Target, m.Sha1, m.Size)) { manual.Remove(m); continue; }
                    if (Net.Matches(found, m.Sha1, m.Size))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(m.Target));
                        File.Copy(found, m.Target, true);
                        Log("Picked up " + m.FileName + " from Downloads.");
                        manual.Remove(m);
                    }
                }
                if (manual.Count == 0) return;
                Status(manual.Count + " mod" + (manual.Count == 1 ? "" : "s") + " to download in your browser...");
                if (!AskManual(manual)) throw new LauncherException("Stopped: " + manual.Count + " mods still need downloading from CurseForge.");
            }
        }

        // ---- 5. The pack's settings ----

        // Copies the pack's overrides (configs, scripts...) into the game
        // folder. The player's own options and server list are kept.
        // Returns the jars it put in mods/ (so they can be removed later).
        List<string> ApplyOverrides(string zip, object manifest)
        {
            string prefix = (Json.Str(manifest, "overrides") ?? "overrides").TrimEnd('/') + "/";
            var modsAdded = new List<string>();
            int count = 0;
            using (var archive = ZipFile.OpenRead(zip))
            {
                foreach (var entry in archive.Entries)
                {
                    if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    string rel = entry.FullName.Substring(prefix.Length);
                    if (rel.Length == 0 || rel.EndsWith("/")) continue;
                    if (rel.Contains("..")) continue;
                    string target = Path.Combine(Settings.GameDir, rel.Replace('/', Path.DirectorySeparatorChar));
                    if (KeepIfPresent.Contains(rel, StringComparer.OrdinalIgnoreCase) && File.Exists(target)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                    count++;
                    if (rel.StartsWith("mods/", StringComparison.OrdinalIgnoreCase)) modsAdded.Add(rel);
                }
            }
            Log("Copied " + count + " modpack files (configs and scripts).");
            return modsAdded;
        }

        // ---- 6. Opening on the server list ----

        // A KubeJS client script (ATM9-TTS has KubeJS): the first time the
        // title screen shows after the game starts, it opens Multiplayer.
        void WriteClientScript()
        {
            string dir = Path.Combine(Settings.GameDir, "kubejs", "client_scripts");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "fourthseal_launcher.js"), ClientScript.Text);
        }

        // ---- 7. The Minecraft Launcher ----

        void UpdateProfiles(string versionId)
        {
            int xmx = MemoryGb();
            foreach (var name in new[] { "launcher_profiles.json", "launcher_profiles_microsoft_store.json" })
            {
                string path = Path.Combine(Settings.MinecraftDir, name);
                if (!File.Exists(path)) continue;
                var data = Json.Obj(Json.Parse(File.ReadAllText(path)));
                if (data == null) throw new LauncherException("Couldn't read " + name + ".");
                string backup = path + ".fourthseal-backup";
                if (!File.Exists(backup)) File.Copy(path, backup);
                var profiles = Json.Obj(data, "profiles");
                if (profiles == null) { profiles = new Dictionary<string, object>(); data["profiles"] = profiles; }
                var profile = Json.Obj(profiles, Settings.ProfileId);
                string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                if (profile == null)
                {
                    profile = new Dictionary<string, object>();
                    profile["created"] = now;
                    profile["icon"] = ProfileIcon.DataUrl;
                    profile["javaArgs"] = "-Xmx" + xmx + "G -XX:+UnlockExperimentalVMOptions -XX:+UseG1GC -XX:G1NewSizePercent=20 -XX:G1ReservePercent=20 -XX:MaxGCPauseMillis=50 -XX:G1HeapRegionSize=32M";
                    profiles[Settings.ProfileId] = profile;
                }
                profile["name"] = Settings.ServerName;
                profile["type"] = "custom";
                profile["lastVersionId"] = versionId;
                profile["gameDir"] = Settings.GameDir;
                profile["lastUsed"] = now; // newest = top of the list
                File.WriteAllText(path, Json.Write(data));
                Log("Minecraft Launcher profile ready in " + name + " (" + xmx + " GB of memory for the game).");
            }
        }

        // Memory for the game: ATM9-TTS wants 6-10 GB.
        int MemoryGb()
        {
            double gb = new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory / (1024.0 * 1024 * 1024);
            if (gb >= 24) return 10;
            if (gb >= 16) return 8;
            if (gb >= 12) return 6;
            Log("Warning: this PC has " + Math.Round(gb) + " GB of memory; the modpack may run slowly or fail to start with less than 12 GB.");
            return Math.Max(4, (int)(gb / 2));
        }

        void OpenMinecraftLauncher()
        {
            string x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string x64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            foreach (var exe in new[] { Path.Combine(x86, @"Minecraft Launcher\MinecraftLauncher.exe"), Path.Combine(x64, @"Minecraft Launcher\MinecraftLauncher.exe") })
            {
                if (File.Exists(exe)) { Process.Start(exe); Log("Opened the Minecraft Launcher."); return; }
            }
            // The Microsoft Store / Xbox app version.
            try
            {
                Process.Start("explorer.exe", @"shell:AppsFolder\Microsoft.4297127D64EC6_8wekyb3d8bbwe!Minecraft");
                Log("Opened the Minecraft Launcher.");
            }
            catch (Exception)
            {
                throw new LauncherException("Everything is ready, but the Minecraft Launcher couldn't be opened - open it yourself and pick the '" + Settings.ServerName + "' profile.");
            }
        }

        // ---- Helpers ----

        object LoadState()
        {
            string path = Path.Combine(Settings.HomeDir, StateFile);
            Directory.CreateDirectory(Settings.HomeDir);
            if (!File.Exists(path)) return new Dictionary<string, object>();
            try { return Json.Parse(File.ReadAllText(path)); }
            catch (Exception) { return new Dictionary<string, object>(); }
        }

        void RunParallel(int count, Action<int> job)
        {
            int done = 0;
            Exception failure = null;
            var options = new ParallelOptions();
            options.MaxDegreeOfParallelism = 6;
            Parallel.For(0, count, options, (i, loop) =>
            {
                try { job(i); }
                catch (Exception e)
                {
                    Interlocked.CompareExchange(ref failure, e, null);
                    loop.Stop();
                    return;
                }
                Progress(Interlocked.Increment(ref done), count);
            });
            if (failure != null)
            {
                if (failure is LauncherException) throw failure;
                throw new LauncherException(failure.Message, failure);
            }
        }

        static string Megabytes(long bytes)
        {
            return Math.Max(1, (int)Math.Round(bytes / (1024.0 * 1024))) + " MB";
        }
    }
}
