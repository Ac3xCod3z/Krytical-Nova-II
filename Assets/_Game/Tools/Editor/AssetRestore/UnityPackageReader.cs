using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace KryticalNova.EditorTools.AssetRestore
{
    /// <summary>
    /// Streams the entries of a .unitypackage, which is a gzipped tar with one folder per asset GUID holding
    /// <c>pathname</c> (where the asset lived), <c>asset</c> (the file), <c>asset.meta</c> and sometimes
    /// <c>preview.png</c>. Nothing is unpacked to disk; the caller reads only the entries it wants.
    /// Pure file IO, so it can run off the main thread.
    /// </summary>
    public sealed class UnityPackageReader : IDisposable
    {
        public enum Part { Other, Pathname, Asset, Meta, Preview }

        /// <summary>The entry the reader is on. <see cref="Read"/> returns its bytes; skip it by calling <see cref="Next"/>.</summary>
        public struct Entry
        {
            public string Guid;
            public Part Part;
            public long Size;
        }

        private const int Block = 512;
        private readonly Stream raw;
        private readonly Stream stream;
        private readonly byte[] header = new byte[Block];
        private readonly byte[] skipBuffer = new byte[81920];
        private long remaining;   // unread bytes of the current entry's data
        private long padding;     // bytes after the data up to the next block
        private bool ended;

        public UnityPackageReader(string packagePath)
        {
            raw = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
            stream = new BufferedStream(new GZipStream(raw, CompressionMode.Decompress), 1 << 20);
        }

        /// <summary>How far through the compressed file the reader is, 0 to 1.</summary>
        public float Progress => raw.Length > 0 ? (float)raw.Position / raw.Length : 1f;

        /// <summary>Moves to the next file entry. Returns false at the end of the package.</summary>
        public bool Next(out Entry entry)
        {
            entry = default;
            string longName = null;
            while (!ended)
            {
                Skip(remaining + padding);
                remaining = padding = 0;

                if (!ReadExactly(header, Block) || IsZero(header)) { ended = true; break; }

                long size = ParseSize(header);
                char type = (char)header[156];
                remaining = size;
                padding = (Block - size % Block) % Block;

                if (type == 'L')                       // GNU long name: the data is the next entry's name
                {
                    longName = Encoding.UTF8.GetString(ReadAll()).TrimEnd('\0');
                    continue;
                }
                if (type == 'x')                       // PAX header: look for a path record
                {
                    string pax = Encoding.UTF8.GetString(ReadAll());
                    string path = PaxPath(pax);
                    if (path != null) longName = path;
                    continue;
                }
                if (type == 'g' || type == '5') { longName = null; continue; }   // global header, directory

                string name = longName ?? HeaderName(header);
                longName = null;
                if (!Split(name, out string guid, out string file)) continue;

                entry.Guid = guid;
                entry.Size = size;
                entry.Part = file == "pathname" ? Part.Pathname
                    : file == "asset" ? Part.Asset
                    : file == "asset.meta" ? Part.Meta
                    : file == "preview.png" ? Part.Preview
                    : Part.Other;
                return true;
            }
            return false;
        }

        /// <summary>Reads the current entry's data whole.</summary>
        public byte[] ReadAll()
        {
            if (remaining > int.MaxValue) throw new IOException("Entry too large to read into memory.");
            var data = new byte[remaining];
            if (!ReadExactly(data, data.Length)) throw new EndOfStreamException("Package ended inside an entry.");
            remaining = 0;
            return data;
        }

        /// <summary>Reads up to <paramref name="count"/> bytes from the start of the current entry (the rest is skipped on Next).</summary>
        public byte[] ReadHead(int count)
        {
            int n = (int)Math.Min(count, remaining);
            var data = new byte[n];
            if (!ReadExactly(data, n)) throw new EndOfStreamException("Package ended inside an entry.");
            remaining -= n;
            return data;
        }

        /// <summary>Copies the rest of the current entry to <paramref name="destination"/>.</summary>
        public void CopyTo(Stream destination)
        {
            while (remaining > 0)
            {
                int n = stream.Read(skipBuffer, 0, (int)Math.Min(skipBuffer.Length, remaining));
                if (n <= 0) throw new EndOfStreamException("Package ended inside an entry.");
                destination.Write(skipBuffer, 0, n);
                remaining -= n;
            }
        }

        /// <summary>The first line of a <c>pathname</c> entry: the asset's path inside the project.</summary>
        public static string PathFrom(byte[] pathnameData)
        {
            string text = Encoding.UTF8.GetString(pathnameData);
            int cut = text.IndexOfAny(new[] { '\n', '\r', '\0' });
            return (cut >= 0 ? text.Substring(0, cut) : text).Trim().Replace('\\', '/');
        }

        public void Dispose()
        {
            stream.Dispose();
            raw.Dispose();
        }

        private void Skip(long count)
        {
            while (count > 0)
            {
                int n = stream.Read(skipBuffer, 0, (int)Math.Min(skipBuffer.Length, count));
                if (n <= 0) { ended = true; return; }
                count -= n;
            }
        }

        private bool ReadExactly(byte[] buffer, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = stream.Read(buffer, read, count - read);
                if (n <= 0) return false;
                read += n;
            }
            return true;
        }

        private static bool IsZero(byte[] block)
        {
            for (int i = 0; i < block.Length; i++) if (block[i] != 0) return false;
            return true;
        }

        private static long ParseSize(byte[] h)
        {
            if ((h[124] & 0x80) != 0)                  // base-256 for entries of 8 GB and more
            {
                long value = h[124] & 0x7F;
                for (int i = 125; i < 136; i++) value = (value << 8) | h[i];
                return value;
            }
            long size = 0;
            for (int i = 124; i < 136; i++)
            {
                byte c = h[i];
                if (c == 0 || c == ' ') { if (size > 0) break; continue; }
                size = size * 8 + (c - '0');
            }
            return size;
        }

        private static string HeaderName(byte[] h)
        {
            string name = Field(h, 0, 100);
            string prefix = Encoding.ASCII.GetString(h, 257, 5) == "ustar" ? Field(h, 345, 155) : "";
            return prefix.Length > 0 ? prefix + "/" + name : name;
        }

        private static string Field(byte[] h, int offset, int length)
        {
            int end = offset;
            while (end < offset + length && h[end] != 0) end++;
            return Encoding.UTF8.GetString(h, offset, end - offset);
        }

        private static string PaxPath(string pax)
        {
            foreach (string record in pax.Split('\n'))
            {
                int space = record.IndexOf(' ');
                if (space < 0) continue;
                string kv = record.Substring(space + 1);
                if (kv.StartsWith("path=", StringComparison.Ordinal)) return kv.Substring(5);
            }
            return null;
        }

        /// <summary>"./0123…/asset" → guid "0123…", file "asset".</summary>
        private static bool Split(string name, out string guid, out string file)
        {
            guid = file = null;
            if (name.StartsWith("./", StringComparison.Ordinal)) name = name.Substring(2);
            int slash = name.IndexOf('/');
            if (slash != 32 || name.Length <= 33) return false;
            guid = name.Substring(0, 32);
            file = name.Substring(33);
            return true;
        }
    }
}
