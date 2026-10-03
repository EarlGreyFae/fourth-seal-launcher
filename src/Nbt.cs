// Minecraft's NBT format (uncompressed, big-endian), enough to read and
// rewrite servers.dat without losing anything already in it. Strings use
// Java's "modified UTF-8", so server names with emoji survive a rewrite.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FourthSealLauncher
{
    class NbtTag
    {
        public byte Type;
        public object Value; // see Read for what each type holds

        public NbtTag(byte type, object value) { Type = type; Value = value; }

        public static NbtTag Str(string s) { return new NbtTag(8, s); }
        public static NbtTag Byte(byte b) { return new NbtTag(1, b); }
        public static NbtTag Compound() { return new NbtTag(10, new List<KeyValuePair<string, NbtTag>>()); }

        public List<KeyValuePair<string, NbtTag>> Entries { get { return (List<KeyValuePair<string, NbtTag>>)Value; } }
        public NbtList List { get { return (NbtList)Value; } }

        public NbtTag Child(string name)
        {
            foreach (var kv in Entries) if (kv.Key == name) return kv.Value;
            return null;
        }

        public void Set(string name, NbtTag tag)
        {
            var entries = Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Key == name) { entries[i] = new KeyValuePair<string, NbtTag>(name, tag); return; }
            }
            entries.Add(new KeyValuePair<string, NbtTag>(name, tag));
        }
    }

    class NbtList
    {
        public byte ElementType;
        public List<NbtTag> Items = new List<NbtTag>();
    }

    static class Nbt
    {
        // Reads a whole file: one named root tag.
        public static NbtTag ReadFile(string path, out string rootName)
        {
            using (var r = new BinaryReader(File.OpenRead(path)))
            {
                byte type = r.ReadByte();
                if (type != 10) throw new InvalidDataException("Not an NBT compound file");
                rootName = ReadString(r);
                return ReadPayload(r, type);
            }
        }

        public static void WriteFile(string path, string rootName, NbtTag root)
        {
            using (var ms = new MemoryStream())
            {
                var w = new BinaryWriter(ms);
                w.Write(root.Type);
                WriteString(w, rootName);
                WritePayload(w, root);
                w.Flush();
                File.WriteAllBytes(path, ms.ToArray());
            }
        }

        static NbtTag ReadPayload(BinaryReader r, byte type)
        {
            switch (type)
            {
                case 1: return new NbtTag(type, r.ReadByte());
                case 2: return new NbtTag(type, (short)ReadBE(r, 2));
                case 3: return new NbtTag(type, (int)ReadBE(r, 4));
                case 4: return new NbtTag(type, ReadBE(r, 8));
                case 5: return new NbtTag(type, r.ReadBytes(4)); // floats/doubles kept as raw bytes
                case 6: return new NbtTag(type, r.ReadBytes(8));
                case 7: { int n = (int)ReadBE(r, 4); return new NbtTag(type, r.ReadBytes(n)); }
                case 8: return new NbtTag(type, ReadString(r));
                case 9:
                    {
                        var list = new NbtList();
                        list.ElementType = r.ReadByte();
                        int n = (int)ReadBE(r, 4);
                        for (int i = 0; i < n; i++) list.Items.Add(ReadPayload(r, list.ElementType));
                        return new NbtTag(type, list);
                    }
                case 10:
                    {
                        var tag = NbtTag.Compound();
                        while (true)
                        {
                            byte child = r.ReadByte();
                            if (child == 0) break;
                            string name = ReadString(r);
                            tag.Entries.Add(new KeyValuePair<string, NbtTag>(name, ReadPayload(r, child)));
                        }
                        return tag;
                    }
                case 11: { int n = (int)ReadBE(r, 4); return new NbtTag(type, r.ReadBytes(n * 4)); }
                case 12: { int n = (int)ReadBE(r, 4); return new NbtTag(type, r.ReadBytes(n * 8)); }
                default: throw new InvalidDataException("Unknown NBT tag type " + type);
            }
        }

        static void WritePayload(BinaryWriter w, NbtTag tag)
        {
            switch (tag.Type)
            {
                case 1: w.Write((byte)tag.Value); break;
                case 2: WriteBE(w, (short)tag.Value, 2); break;
                case 3: WriteBE(w, (int)tag.Value, 4); break;
                case 4: WriteBE(w, (long)tag.Value, 8); break;
                case 5:
                case 6: w.Write((byte[])tag.Value); break;
                case 7: { var b = (byte[])tag.Value; WriteBE(w, b.Length, 4); w.Write(b); break; }
                case 8: WriteString(w, (string)tag.Value); break;
                case 9:
                    {
                        var list = tag.List;
                        w.Write(list.Items.Count == 0 && list.ElementType == 0 ? (byte)0 : list.ElementType);
                        WriteBE(w, list.Items.Count, 4);
                        foreach (var item in list.Items) WritePayload(w, item);
                        break;
                    }
                case 10:
                    foreach (var kv in tag.Entries)
                    {
                        w.Write(kv.Value.Type);
                        WriteString(w, kv.Key);
                        WritePayload(w, kv.Value);
                    }
                    w.Write((byte)0);
                    break;
                case 11: { var b = (byte[])tag.Value; WriteBE(w, b.Length / 4, 4); w.Write(b); break; }
                case 12: { var b = (byte[])tag.Value; WriteBE(w, b.Length / 8, 4); w.Write(b); break; }
                default: throw new InvalidDataException("Unknown NBT tag type " + tag.Type);
            }
        }

        static long ReadBE(BinaryReader r, int bytes)
        {
            long v = 0;
            for (int i = 0; i < bytes; i++) v = (v << 8) | r.ReadByte();
            // Sign-extend shorts and ints.
            if (bytes < 8)
            {
                int shift = 64 - bytes * 8;
                v = (v << shift) >> shift;
            }
            return v;
        }

        static void WriteBE(BinaryWriter w, long v, int bytes)
        {
            for (int i = bytes - 1; i >= 0; i--) w.Write((byte)((v >> (i * 8)) & 0xFF));
        }

        // Java modified UTF-8 with an unsigned 16-bit length.
        static string ReadString(BinaryReader r)
        {
            int len = (int)((r.ReadByte() << 8) | r.ReadByte());
            var bytes = r.ReadBytes(len);
            var sb = new StringBuilder();
            int i = 0;
            while (i < bytes.Length)
            {
                int b = bytes[i];
                if (b < 0x80) { sb.Append((char)b); i += 1; }
                else if ((b & 0xE0) == 0xC0 && i + 1 < bytes.Length) { sb.Append((char)(((b & 0x1F) << 6) | (bytes[i + 1] & 0x3F))); i += 2; }
                else if (i + 2 < bytes.Length) { sb.Append((char)(((b & 0x0F) << 12) | ((bytes[i + 1] & 0x3F) << 6) | (bytes[i + 2] & 0x3F))); i += 3; }
                else { i += 1; }
            }
            return sb.ToString();
        }

        static void WriteString(BinaryWriter w, string s)
        {
            var bytes = new List<byte>();
            foreach (char c in s)
            {
                if (c >= 0x0001 && c <= 0x007F) bytes.Add((byte)c);
                else if (c <= 0x07FF)
                {
                    bytes.Add((byte)(0xC0 | (c >> 6)));
                    bytes.Add((byte)(0x80 | (c & 0x3F)));
                }
                else
                {
                    bytes.Add((byte)(0xE0 | (c >> 12)));
                    bytes.Add((byte)(0x80 | ((c >> 6) & 0x3F)));
                    bytes.Add((byte)(0x80 | (c & 0x3F)));
                }
            }
            if (bytes.Count > 65535) throw new InvalidDataException("NBT string too long");
            w.Write((byte)(bytes.Count >> 8));
            w.Write((byte)(bytes.Count & 0xFF));
            w.Write(bytes.ToArray());
        }
    }

    // servers.dat: { servers: [ {name, ip, icon?, acceptTextures?}, ... ] }
    static class ServerList
    {
        // Puts the server first in the list (updating it if it's already
        // there, so its saved icon and settings are kept). Returns true if
        // anything changed.
        public static bool EnsureServer(string path, string name, string address)
        {
            NbtTag root;
            string rootName = "";
            if (File.Exists(path))
            {
                try { root = Nbt.ReadFile(path, out rootName); }
                catch (Exception)
                {
                    // Unreadable: keep a copy and start a new list.
                    File.Copy(path, path + ".unreadable", true);
                    root = NbtTag.Compound();
                }
            }
            else root = NbtTag.Compound();

            var serversTag = root.Child("servers");
            if (serversTag == null || serversTag.Type != 9)
            {
                var list = new NbtList();
                list.ElementType = 10;
                serversTag = new NbtTag(9, list);
                root.Set("servers", serversTag);
            }
            var items = serversTag.List.Items;
            serversTag.List.ElementType = 10;

            NbtTag ours = null;
            int at = -1;
            for (int i = 0; i < items.Count; i++)
            {
                var ipTag = items[i].Type == 10 ? items[i].Child("ip") : null;
                var nameTag = items[i].Type == 10 ? items[i].Child("name") : null;
                bool sameIp = ipTag != null && string.Equals((string)ipTag.Value, address, StringComparison.OrdinalIgnoreCase);
                bool sameName = nameTag != null && (string)nameTag.Value == name;
                if (sameIp || sameName) { ours = items[i]; at = i; break; }
            }
            if (ours != null && at == 0 && TextOf(ours, "ip") == address && TextOf(ours, "name") == name)
                return false;
            if (ours == null) ours = NbtTag.Compound();
            else items.RemoveAt(at);
            ours.Set("name", NbtTag.Str(name));
            ours.Set("ip", NbtTag.Str(address));
            items.Insert(0, ours);

            if (File.Exists(path)) File.Copy(path, path + "_old", true);
            Nbt.WriteFile(path, rootName, root);
            return true;
        }

        static string TextOf(NbtTag compound, string key)
        {
            var tag = compound.Child(key);
            return tag != null && tag.Type == 8 ? (string)tag.Value : null;
        }

        // For the self-test: the list's names and addresses, in order.
        public static List<string> Describe(string path)
        {
            string rootName;
            var root = Nbt.ReadFile(path, out rootName);
            var result = new List<string>();
            var servers = root.Child("servers");
            if (servers == null) return result;
            foreach (var item in servers.List.Items)
            {
                var n = item.Child("name");
                var ip = item.Child("ip");
                result.Add((n == null ? "" : (string)n.Value) + " @ " + (ip == null ? "" : (string)ip.Value));
            }
            return result;
        }
    }
}
