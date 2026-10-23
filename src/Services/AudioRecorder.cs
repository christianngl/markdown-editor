using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MarkdownEditor.Services
{
    /// <summary>
    /// Mikrofonaufnahme über die Windows-Wave-API (winmm.dll) – ohne zusätzliche Bibliotheken.
    /// Nimmt direkt im Whisper-Format auf (16 kHz, mono, 16 bit PCM) und speichert als WAV.
    /// </summary>
    public sealed class AudioRecorder : IDisposable
    {
        public const int SampleRate = 16000;
        const int BufferCount = 4;
        const int BufferMs = 100;
        const int BufferBytes = SampleRate * 2 * BufferMs / 1000;
        const int WaveMapper = -1;
        const int CallbackEvent = 0x00050000;
        const int WhdrDone = 0x00000001;

        IntPtr _handle;
        readonly AutoResetEvent _event = new AutoResetEvent(false);
        readonly MemoryStream _data = new MemoryStream();
        readonly List<IntPtr> _headers = new List<IntPtr>();
        readonly List<GCHandle> _pins = new List<GCHandle>();
        Thread _thread;
        volatile bool _running;
        volatile int _level;

        /// <summary>Lautstärke des zuletzt aufgenommenen Puffers (0–100).</summary>
        public int Level { get { return _level; } }

        public TimeSpan Duration
        {
            get { lock (_data) return TimeSpan.FromSeconds(_data.Length / (double)(SampleRate * 2)); }
        }

        /// <summary>Namen der Aufnahmegeräte; der Index entspricht der Geräte-ID.</summary>
        public static List<string> GetDevices()
        {
            var list = new List<string>();
            int n = waveInGetNumDevs();
            for (int i = 0; i < n; i++)
            {
                var caps = new WaveInCaps();
                list.Add(waveInGetDevCaps((IntPtr)i, ref caps, Marshal.SizeOf(typeof(WaveInCaps))) == 0 ? caps.szPname : "Gerät " + (i + 1));
            }
            return list;
        }

        /// <summary>Startet die Aufnahme; <paramref name="deviceId"/> −1 = Standardgerät.</summary>
        public void Start(int deviceId)
        {
            if (_running) return;
            if (waveInGetNumDevs() == 0) throw new AudioException("Kein Mikrofon gefunden.");
            if (deviceId >= waveInGetNumDevs()) deviceId = WaveMapper;

            var fmt = new WaveFormat { wFormatTag = 1, nChannels = 1, nSamplesPerSec = SampleRate, wBitsPerSample = 16, nBlockAlign = 2, nAvgBytesPerSec = SampleRate * 2 };
            Check(waveInOpen(out _handle, (IntPtr)deviceId, ref fmt, _event.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CallbackEvent), "Mikrofon öffnen");
            try
            {
                int headerSize = Marshal.SizeOf(typeof(WaveHdr));
                for (int i = 0; i < BufferCount; i++)
                {
                    GCHandle pin = GCHandle.Alloc(new byte[BufferBytes], GCHandleType.Pinned);
                    _pins.Add(pin);
                    var hdr = new WaveHdr { lpData = pin.AddrOfPinnedObject(), dwBufferLength = BufferBytes };
                    IntPtr p = Marshal.AllocHGlobal(headerSize);
                    Marshal.StructureToPtr(hdr, p, false);
                    _headers.Add(p);
                    Check(waveInPrepareHeader(_handle, p, headerSize), "Puffer vorbereiten");
                    Check(waveInAddBuffer(_handle, p, headerSize), "Puffer einreihen");
                }
                _running = true;
                Check(waveInStart(_handle), "Aufnahme starten");
            }
            catch
            {
                _running = false;
                Release();
                throw;
            }
            _thread = new Thread(Pump) { IsBackground = true, Name = "AudioRecorder" };
            _thread.Start();
        }

        /// <summary>Holt fertige Puffer ab und reiht sie wieder ein (nicht im Treiber-Callback, siehe MSDN).</summary>
        void Pump()
        {
            int headerSize = Marshal.SizeOf(typeof(WaveHdr));
            while (_running)
            {
                _event.WaitOne(200);
                foreach (IntPtr p in _headers)
                {
                    var hdr = (WaveHdr)Marshal.PtrToStructure(p, typeof(WaveHdr));
                    if ((hdr.dwFlags & WhdrDone) == 0) continue;
                    Collect(hdr);
                    hdr.dwFlags &= ~WhdrDone;
                    hdr.dwBytesRecorded = 0;
                    Marshal.StructureToPtr(hdr, p, false);
                    if (_running) waveInAddBuffer(_handle, p, headerSize);
                }
            }
        }

        void Collect(WaveHdr hdr)
        {
            int n = hdr.dwBytesRecorded;
            if (n <= 0) return;
            var buf = new byte[n];
            Marshal.Copy(hdr.lpData, buf, 0, n);
            lock (_data) _data.Write(buf, 0, n);
            _level = PeakLevel(buf, n);
        }

        /// <summary>Beendet die Aufnahme und speichert sie als WAV-Datei.</summary>
        public void StopAndSave(string wavPath)
        {
            Stop();
            byte[] pcm;
            lock (_data) pcm = _data.ToArray();
            File.WriteAllBytes(wavPath, BuildWav(pcm));
        }

        /// <summary>Beendet die Aufnahme und verwirft sie.</summary>
        public void Cancel()
        {
            Stop();
            lock (_data) _data.SetLength(0);
        }

        void Stop()
        {
            if (_handle == IntPtr.Zero) return;
            // Erst den Pump-Thread beenden (danach reiht niemand mehr Puffer ein), dann den Treiber stoppen.
            _running = false;
            if (_thread != null) { _event.Set(); _thread.Join(2000); _thread = null; }
            waveInStop(_handle);
            waveInReset(_handle); // gibt alle Puffer mit WHDR_DONE zurück
            // Restdaten aus den zurückgegebenen Puffern übernehmen
            foreach (IntPtr p in _headers)
            {
                var hdr = (WaveHdr)Marshal.PtrToStructure(p, typeof(WaveHdr));
                if ((hdr.dwFlags & WhdrDone) != 0) Collect(hdr);
            }
            Release();
        }

        void Release()
        {
            int headerSize = Marshal.SizeOf(typeof(WaveHdr));
            if (_handle != IntPtr.Zero) waveInReset(_handle);
            foreach (IntPtr p in _headers)
            {
                if (_handle != IntPtr.Zero) waveInUnprepareHeader(_handle, p, headerSize);
                Marshal.FreeHGlobal(p);
            }
            _headers.Clear();
            foreach (GCHandle h in _pins) h.Free();
            _pins.Clear();
            if (_handle != IntPtr.Zero) { waveInClose(_handle); _handle = IntPtr.Zero; }
        }

        public void Dispose()
        {
            Cancel();
            _event.Dispose();
        }

        /// <summary>WAV-Datei (RIFF, PCM 16 bit mono 16 kHz) aus Rohdaten.</summary>
        public static byte[] BuildWav(byte[] pcm)
        {
            var ms = new MemoryStream(44 + pcm.Length);
            var w = new BinaryWriter(ms);
            w.Write(Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + pcm.Length);
            w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt "));
            w.Write(16);
            w.Write((short)1);              // PCM
            w.Write((short)1);              // mono
            w.Write(SampleRate);
            w.Write(SampleRate * 2);        // Bytes pro Sekunde
            w.Write((short)2);              // Block-Ausrichtung
            w.Write((short)16);             // Bits pro Sample
            w.Write(Encoding.ASCII.GetBytes("data"));
            w.Write(pcm.Length);
            w.Write(pcm);
            w.Flush();
            return ms.ToArray();
        }

        static int PeakLevel(byte[] buf, int n)
        {
            int peak = 0;
            for (int i = 0; i + 1 < n; i += 2)
            {
                int v = Math.Abs((int)BitConverter.ToInt16(buf, i));
                if (v > peak) peak = v;
            }
            return Math.Min(100, peak * 100 / 32768);
        }

        static void Check(int result, string action)
        {
            if (result == 0) return;
            string msg;
            switch (result)
            {
                case 2: case 6: msg = "Kein passendes Mikrofon gefunden."; break;               // BADDEVICEID / NODRIVER
                case 4: msg = "Das Mikrofon wird bereits von einem anderen Programm verwendet."; break; // ALLOCATED
                case 7: msg = "Kein Speicher für die Aufnahme verfügbar."; break;
                case 32: msg = "Das Mikrofon unterstützt das Aufnahmeformat (16 kHz mono) nicht."; break;
                default:
                    msg = "Zugriff auf das Mikrofon nicht möglich.\n\nBitte prüfen: Windows-Einstellungen → Datenschutz und Sicherheit → Mikrofon → " +
                          "„Desktop-Apps den Zugriff auf das Mikrofon erlauben“ muss eingeschaltet sein.";
                    break;
            }
            throw new AudioException(msg + "\n(" + action + ", Fehlercode " + result + ")");
        }

        // ---------------------------------------------------------------- winmm.dll

        [StructLayout(LayoutKind.Sequential)]
        struct WaveFormat
        {
            public short wFormatTag;
            public short nChannels;
            public int nSamplesPerSec;
            public int nAvgBytesPerSec;
            public short nBlockAlign;
            public short wBitsPerSample;
            public short cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WaveHdr
        {
            public IntPtr lpData;
            public int dwBufferLength;
            public int dwBytesRecorded;
            public IntPtr dwUser;
            public int dwFlags;
            public int dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WaveInCaps
        {
            public short wMid;
            public short wPid;
            public int vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public int dwFormats;
            public short wChannels;
            public short wReserved1;
        }

        [DllImport("winmm.dll")] static extern int waveInGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "waveInGetDevCapsW")] static extern int waveInGetDevCaps(IntPtr id, ref WaveInCaps caps, int size);
        [DllImport("winmm.dll")] static extern int waveInOpen(out IntPtr handle, IntPtr deviceId, ref WaveFormat fmt, IntPtr callback, IntPtr instance, int flags);
        [DllImport("winmm.dll")] static extern int waveInPrepareHeader(IntPtr handle, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInUnprepareHeader(IntPtr handle, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInAddBuffer(IntPtr handle, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInStart(IntPtr handle);
        [DllImport("winmm.dll")] static extern int waveInStop(IntPtr handle);
        [DllImport("winmm.dll")] static extern int waveInReset(IntPtr handle);
        [DllImport("winmm.dll")] static extern int waveInClose(IntPtr handle);
    }

    public sealed class AudioException : Exception
    {
        public AudioException(string message) : base(message) { }
    }
}
