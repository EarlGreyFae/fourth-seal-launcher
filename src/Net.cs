// Downloads with retries and hash checks.
using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace FourthSealLauncher
{
    static class Net
    {
        public const string UserAgent = "TheFourthSealLauncher/1.0";

        static Net()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.DefaultConnectionLimit = 16;
            ServicePointManager.Expect100Continue = false;
        }

        public static void Init() { /* runs the static constructor */ }

        static HttpWebRequest Request(string url, string method, string apiKey)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.UserAgent = UserAgent;
            req.Accept = "application/json, */*";
            req.Timeout = 60000;
            req.ReadWriteTimeout = 120000;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            if (!string.IsNullOrEmpty(apiKey)) req.Headers["x-api-key"] = apiKey;
            return req;
        }

        public static string GetText(string url, string apiKey)
        {
            return WithRetries(url, delegate
            {
                var req = Request(url, "GET", apiKey);
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return reader.ReadToEnd();
            });
        }

        public static string PostJson(string url, string body, string apiKey)
        {
            return WithRetries(url, delegate
            {
                var req = Request(url, "POST", apiKey);
                req.ContentType = "application/json";
                var bytes = Encoding.UTF8.GetBytes(body);
                req.ContentLength = bytes.Length;
                using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return reader.ReadToEnd();
            });
        }

        // Downloads to path (via a .part file). expectedSha1 may be null.
        public static void Download(string url, string path, string expectedSha1, long expectedSize)
        {
            WithRetries(url, delegate
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string part = path + ".part";
                var req = Request(url, "GET", null);
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var input = resp.GetResponseStream())
                using (var output = File.Create(part))
                    input.CopyTo(output, 81920);
                if (expectedSize > 0 && new FileInfo(part).Length != expectedSize)
                    throw new IOException("Download was the wrong size: " + Path.GetFileName(path));
                if (!string.IsNullOrEmpty(expectedSha1) && !string.Equals(Sha1(part), expectedSha1, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Download didn't match its checksum: " + Path.GetFileName(path));
                if (File.Exists(path)) File.Delete(path);
                File.Move(part, path);
                return null;
            });
        }

        public static string Sha1(string path)
        {
            using (var sha = SHA1.Create())
            using (var s = File.OpenRead(path))
            {
                var hash = sha.ComputeHash(s);
                var sb = new StringBuilder();
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // True if the file is already there and matches.
        public static bool Matches(string path, string sha1, long size)
        {
            if (!File.Exists(path)) return false;
            if (size > 0 && new FileInfo(path).Length != size) return false;
            if (!string.IsNullOrEmpty(sha1)) return string.Equals(Sha1(path), sha1, StringComparison.OrdinalIgnoreCase);
            return true;
        }

        delegate string Attempt();

        static string WithRetries(string url, Attempt attempt)
        {
            Exception last = null;
            for (int i = 0; i < 4; i++)
            {
                try { return attempt(); }
                catch (WebException e)
                {
                    last = e;
                    var resp = e.Response as HttpWebResponse;
                    // Not worth retrying: wrong key, not found, bad request.
                    if (resp != null && ((int)resp.StatusCode == 400 || (int)resp.StatusCode == 401 || (int)resp.StatusCode == 403 || (int)resp.StatusCode == 404))
                        throw new LauncherException(DescribeHttpError(url, resp), e);
                }
                catch (IOException e) { last = e; }
                Thread.Sleep(1000 * (i + 1));
            }
            throw new LauncherException("Couldn't download " + ShortUrl(url) + " (" + (last == null ? "unknown error" : last.Message) + "). Check your internet connection and try again.", last);
        }

        static string DescribeHttpError(string url, HttpWebResponse resp)
        {
            int code = (int)resp.StatusCode;
            bool curseForgeApi = url.IndexOf("curseforge", StringComparison.OrdinalIgnoreCase) >= 0 || url.IndexOf("/v1/mods", StringComparison.OrdinalIgnoreCase) >= 0;
            if (curseForgeApi && (code == 403 || code == 401))
                return "CurseForge refused the request (403) - the launcher's CurseForge API key is missing or not approved yet.";
            return "The server said " + code + " " + resp.StatusDescription + " for " + ShortUrl(url) + ".";
        }

        static string ShortUrl(string url)
        {
            try { var u = new Uri(url); return u.Host + u.AbsolutePath; }
            catch (Exception) { return url; }
        }
    }

    // A problem to show the player in plain words.
    class LauncherException : Exception
    {
        public LauncherException(string message) : base(message) { }
        public LauncherException(string message, Exception inner) : base(message, inner) { }
    }
}
