// The parts of the CurseForge API the launcher uses
// (https://docs.curseforge.com/rest-api/). Minecraft is game 432;
// modpacks are class 4471.
using System;
using System.Collections.Generic;
using System.Linq;

namespace FourthSealLauncher
{
    class CurseForge
    {
        public const int MinecraftGameId = 432;
        public const int ModpackClassId = 4471;
        readonly string baseUrl;
        readonly string key;

        public CurseForge(string baseUrl, string key)
        {
            this.baseUrl = baseUrl.TrimEnd('/');
            this.key = key;
        }

        object Get(string path) { return Json.Parse(Net.GetText(baseUrl + path, key)); }
        object Post(string path, object body) { return Json.Parse(Net.PostJson(baseUrl + path, Json.Write(body), key)); }

        public long FindModpackId(string slug)
        {
            var result = Get("/v1/mods/search?gameId=" + MinecraftGameId + "&classId=" + ModpackClassId + "&slug=" + Uri.EscapeDataString(slug));
            foreach (var mod in Json.Arr(result, "data"))
                if (Json.Str(mod, "slug") == slug) return Json.Long(mod, "id");
            throw new LauncherException("Couldn't find the modpack '" + slug + "' on CurseForge.");
        }

        public object GetFile(long modId, long fileId)
        {
            return Json.Get(Get("/v1/mods/" + modId + "/files/" + fileId), "data");
        }

        // Every file of a mod, newest first.
        public List<object> AllFiles(long modId)
        {
            var files = new List<object>();
            int index = 0;
            while (true)
            {
                var page = Get("/v1/mods/" + modId + "/files?index=" + index + "&pageSize=50");
                var data = Json.Arr(page, "data");
                files.AddRange(data);
                long total = Json.Long(Json.Get(page, "pagination"), "totalCount");
                index += data.Count;
                if (data.Count == 0 || index >= total || index >= 9950) break;
            }
            return files.OrderByDescending(f => Json.Str(f, "fileDate")).ToList();
        }

        // File details for many files at once: fileId -> file.
        public Dictionary<long, object> GetFiles(IEnumerable<long> fileIds)
        {
            var result = new Dictionary<long, object>();
            foreach (var batch in Batches(fileIds, 200))
            {
                var body = new Dictionary<string, object>();
                body["fileIds"] = batch;
                foreach (var f in Json.Arr(Post("/v1/mods/files", body), "data"))
                    result[Json.Long(f, "id")] = f;
            }
            return result;
        }

        // Mod details for many mods at once: modId -> mod.
        public Dictionary<long, object> GetMods(IEnumerable<long> modIds)
        {
            var result = new Dictionary<long, object>();
            foreach (var batch in Batches(modIds, 200))
            {
                var body = new Dictionary<string, object>();
                body["modIds"] = batch;
                foreach (var m in Json.Arr(Post("/v1/mods", body), "data"))
                    result[Json.Long(m, "id")] = m;
            }
            return result;
        }

        static IEnumerable<long[]> Batches(IEnumerable<long> ids, int size)
        {
            var all = ids.Distinct().ToList();
            for (int i = 0; i < all.Count; i += size)
                yield return all.Skip(i).Take(size).ToArray();
        }

        // A file's SHA-1 (CurseForge hash algo 1), or null.
        public static string Sha1Of(object file)
        {
            foreach (var h in Json.Arr(file, "hashes"))
                if (Json.Long(h, "algo") == 1) return Json.Str(h, "value");
            return null;
        }
    }
}
