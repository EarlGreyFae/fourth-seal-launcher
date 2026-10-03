// Small helpers over JavaScriptSerializer (part of .NET Framework), which
// reads JSON into Dictionary<string, object> / object[] / numbers / strings.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace FourthSealLauncher
{
    static class Json
    {
        static JavaScriptSerializer Serializer()
        {
            var s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            s.RecursionLimit = 512;
            return s;
        }

        public static object Parse(string text)
        {
            return Serializer().DeserializeObject(text);
        }

        public static string Write(object value)
        {
            return Serializer().Serialize(value);
        }

        public static Dictionary<string, object> Obj(object o)
        {
            return o as Dictionary<string, object>;
        }

        public static Dictionary<string, object> Obj(object o, string key)
        {
            return Obj(Get(o, key));
        }

        public static object Get(object o, string key)
        {
            var d = Obj(o);
            object v;
            if (d == null || !d.TryGetValue(key, out v)) return null;
            return v;
        }

        public static string Str(object o, string key)
        {
            var v = Get(o, key);
            return v == null ? null : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
        }

        public static long Long(object o, string key)
        {
            var v = Get(o, key);
            if (v == null) return 0;
            try { return Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture); }
            catch (Exception) { return 0; }
        }

        public static bool Bool(object o, string key, bool fallback)
        {
            var v = Get(o, key);
            if (v is bool) return (bool)v;
            return fallback;
        }

        public static List<object> Arr(object o, string key)
        {
            return List(Get(o, key));
        }

        public static List<object> List(object v)
        {
            var result = new List<object>();
            var e = v as IEnumerable;
            if (e == null || v is string || v is IDictionary) return result;
            foreach (var item in e) result.Add(item);
            return result;
        }
    }
}
