using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MarkdownEditor.Services
{
    /// <summary>
    /// Verschlüsselt Texte mit der Windows-DPAPI für den aktuellen Benutzer (z. B. den API-Schlüssel in settings.ini).
    /// Direkt über crypt32.dll, damit weder System.Security.dll noch ein NuGet-Paket (.NET 8) nötig ist.
    /// </summary>
    public static class Dpapi
    {
        [StructLayout(LayoutKind.Sequential)]
        struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        const int UiForbidden = 0x1;

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CryptProtectData(ref DataBlob dataIn, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

        [DllImport("kernel32.dll")]
        static extern IntPtr LocalFree(IntPtr mem);

        /// <summary>Verschlüsselt <paramref name="text"/> und liefert Base64; bei Fehlern null.</summary>
        public static string Protect(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            byte[] result = Transform(Encoding.UTF8.GetBytes(text), true);
            return result == null ? null : Convert.ToBase64String(result);
        }

        /// <summary>Entschlüsselt einen mit <see cref="Protect"/> erzeugten Wert; bei Fehlern null.</summary>
        public static string Unprotect(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return null;
            byte[] data;
            try { data = Convert.FromBase64String(base64); }
            catch (FormatException) { return null; }
            byte[] result = Transform(data, false);
            return result == null ? null : Encoding.UTF8.GetString(result);
        }

        static byte[] Transform(byte[] input, bool protect)
        {
            var inBlob = new DataBlob { Size = input.Length, Data = Marshal.AllocHGlobal(input.Length) };
            var outBlob = new DataBlob();
            try
            {
                Marshal.Copy(input, 0, inBlob.Data, input.Length);
                bool ok = protect
                    ? CryptProtectData(ref inBlob, "MarkdownEditor", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref outBlob)
                    : CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref outBlob);
                if (!ok) return null;
                var output = new byte[outBlob.Size];
                Marshal.Copy(outBlob.Data, output, 0, outBlob.Size);
                return output;
            }
            finally
            {
                Marshal.FreeHGlobal(inBlob.Data);
                if (outBlob.Data != IntPtr.Zero) LocalFree(outBlob.Data);
            }
        }
    }
}
