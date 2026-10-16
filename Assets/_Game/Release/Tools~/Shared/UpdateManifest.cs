using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace KryticalNova.Release
{
    /// <summary>
    /// The update list: every file of one game build with its size and SHA-256, and the launcher of that build. It is
    /// published as <c>manifest.signed</c>: an ECDSA P-256 signature of the list's exact bytes (made with the owner's
    /// key) on the first line, the list after it. One file, because the content network caches each address for a few
    /// minutes on its own: a list and a signature kept apart could arrive from different builds. A launcher reads
    /// nothing from a list whose signature does not check out against the public key built into it. Each file is stored once, compressed, under its own hash (<see cref="PackedPath"/>), so a file
    /// that did not change between builds is neither uploaded nor downloaded again.
    /// </summary>
    [DataContract]
    public sealed class UpdateManifest
    {
        public const string FileName = "manifest.json";
        public const string SignedName = "manifest.signed";
        public const int CurrentFormat = 1;

        [DataMember(Order = 1)] public int format = CurrentFormat;
        [DataMember(Order = 2)] public int build;
        [DataMember(Order = 3)] public string version = "";
        [DataMember(Order = 4)] public string created = "";
        [DataMember(Order = 5)] public string gameExe = "";
        [DataMember(Order = 6)] public List<UpdateFile> files = new List<UpdateFile>();
        [DataMember(Order = 7)] public UpdateFile launcher;

        /// <summary>Where a file's compressed copy is kept, by its hash.</summary>
        public static string PackedPath(string sha256) => "files/" + sha256 + ".gz";

        public byte[] ToBytes()
        {
            var serializer = new DataContractJsonSerializer(typeof(UpdateManifest));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, this);
                return stream.ToArray();
            }
        }

        /// <summary>The signature line, then the list.</summary>
        public static byte[] Sign(byte[] manifest, string signature)
        {
            byte[] head = Encoding.ASCII.GetBytes(signature.Trim() + "\n");
            var all = new byte[head.Length + manifest.Length];
            Buffer.BlockCopy(head, 0, all, 0, head.Length);
            Buffer.BlockCopy(manifest, 0, all, head.Length, manifest.Length);
            return all;
        }

        /// <summary>Splits a signed list into its signature and the exact bytes that were signed.</summary>
        public static bool Split(byte[] signed, out string signature, out byte[] manifest)
        {
            signature = null;
            manifest = null;
            int newline = Array.IndexOf(signed, (byte)'\n');
            if (newline <= 0 || newline > 200) return false;
            signature = Encoding.ASCII.GetString(signed, 0, newline).Trim();
            manifest = new byte[signed.Length - newline - 1];
            Buffer.BlockCopy(signed, newline + 1, manifest, 0, manifest.Length);
            return true;
        }

        public static UpdateManifest FromBytes(byte[] bytes)
        {
            var serializer = new DataContractJsonSerializer(typeof(UpdateManifest));
            using (var stream = new MemoryStream(bytes))
            {
                var manifest = (UpdateManifest)serializer.ReadObject(stream);
                if (manifest.files == null) manifest.files = new List<UpdateFile>();
                return manifest;
            }
        }
    }

    [DataContract]
    public sealed class UpdateFile
    {
        /// <summary>Relative to the game's folder, with forward slashes. For the launcher: its file name.</summary>
        [DataMember(Order = 1)] public string path = "";
        [DataMember(Order = 2)] public long size;
        [DataMember(Order = 3)] public string sha256 = "";
        /// <summary>The size of the compressed copy that is downloaded.</summary>
        [DataMember(Order = 4)] public long packedSize;
    }

    /// <summary>Hashes, signatures and paths shared by the launcher and the release tool.</summary>
    public static class UpdateCrypto
    {
        public static string Sha256Hex(Stream stream)
        {
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(stream));
        }

        public static string Sha256Hex(string file)
        {
            using (var stream = File.OpenRead(file)) return Sha256Hex(stream);
        }

        public static string Hex(byte[] bytes)
        {
            var text = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) text.Append(b.ToString("x2"));
            return text.ToString();
        }

        /// <summary>A public key as base64 of its X and Y (32 bytes each), the form built into the launcher.</summary>
        public static ECDsa PublicKey(string base64)
        {
            byte[] q = Convert.FromBase64String(base64);
            if (q.Length != 64) throw new ArgumentException("A public key is 64 bytes.");
            var x = new byte[32];
            var y = new byte[32];
            Buffer.BlockCopy(q, 0, x, 0, 32);
            Buffer.BlockCopy(q, 32, y, 0, 32);
            return ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = x, Y = y } });
        }

        /// <summary>True when <paramref name="signature"/> (base64) is the key's signature of these exact bytes.</summary>
        public static bool Verify(string publicKeyBase64, byte[] data, string signature)
        {
            if (string.IsNullOrWhiteSpace(publicKeyBase64) || string.IsNullOrWhiteSpace(signature)) return false;
            try
            {
                using (ECDsa key = PublicKey(publicKeyBase64))
                    return key.VerifyData(data, Convert.FromBase64String(signature.Trim()), HashAlgorithmName.SHA256);
            }
            catch (FormatException)
            {
                return false;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        /// <summary>A relative path that stays inside the folder it is joined to, or null.</summary>
        public static string SafeJoin(string folder, string relative)
        {
            if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative) || relative.Contains("..") || relative.Contains(":")) return null;
            string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
    }
}
