using System;
using System.IO;

namespace JetDatabaseReader
{
    /// <summary>
    /// Configuration options for opening a JET database with <see cref="AccessReader"/>.
    /// </summary>
    public sealed class AccessReaderOptions
    {
        /// <summary>Maximum number of pages to keep in cache. 0 = unlimited, -1 = disabled. Default: 256 (1 MB for 4K pages).</summary>
        public int PageCacheSize { get; set; } = 256;

        /// <summary>When true, logs verbose diagnostic information. Default: false.</summary>
        public bool DiagnosticsEnabled { get; set; }

        /// <summary>
        /// Has no effect. Nothing reads this; page reads are serialised on the shared file handle.
        /// Kept so existing code keeps compiling.
        /// </summary>
        [Obsolete("Has no effect — page reads are serialised on the shared file handle.")]
        public bool ParallelPageReadsEnabled { get; set; }

        /// <summary>
        /// Database password, for a Jet4 (.mdb) database that has one set. Default: null.
        ///
        /// Note that a Jet4 database password is access control, not encryption: the page data is
        /// stored in plain text and this library could read it either way. The password is
        /// verified so that callers are not silently granted access they did not ask for.
        ///
        /// This does not open an ACE (.accdb) database encrypted with "Encrypt with Password" —
        /// those have genuinely encrypted pages and are still unsupported.
        /// </summary>
        public string Password { get; set; }

        /// <summary>When true, validates the database format on open. Default: true.</summary>
        public bool ValidateOnOpen { get; set; } = true;

        /// <summary>
        /// When true, Text and Memo columns are decoded so that every byte value round-trips:
        /// byte n always becomes the character U+00nn. Default: false, which decodes through the
        /// database's ANSI code page as before.
        ///
        /// For a database that really does hold text, leave this alone — the ANSI code page is
        /// what the data was written with, and it is what renders a name correctly.
        ///
        /// Set it when an application has stored *binary* in Text or Memo columns, which was
        /// ordinary practice in VB6 and Access-era software: a value written with Chr$(n) and
        /// read back with Asc() is a byte array, not a string. Windows-1252 cannot carry one
        /// intact — 27 of its 256 byte values decode to a different code point (0x80 becomes the
        /// euro sign, 0x92 a curly quote), so those bytes come back as something else and
        /// nothing reports an error, because the result is still a perfectly valid string.
        ///
        /// This changes only which encoding is used for Text and Memo. It does not change how any
        /// column is located, sized or read.
        ///
        /// In practice it reaches Jet3 and little else: Jet4 and ACE store Text and Memo as
        /// UCS-2, which is read through <see cref="System.Text.Encoding.Unicode"/> and never
        /// touches the ANSI decode this replaces. A Jet4 column holding real curly quotes reads
        /// the same either way, as it should.
        /// </summary>
        public bool PreserveTextBytes { get; set; }

        /// <summary>
        /// How OLE Object columns are rendered. Default: <see cref="OleObjectMode.DataUri"/>.
        /// Set to <see cref="OleObjectMode.Placeholder"/> when the payloads are not needed —
        /// it skips both the base64 encoding and the LVAL page reads behind it.
        /// </summary>
        public OleObjectMode OleObjectMode { get; set; } = OleObjectMode.DataUri;

        /// <summary>
        /// FileStream buffer size in bytes. Default: 65536.
        /// Reads are one page at a seeked offset, so a buffer larger than the page size lets a
        /// front-to-back scan serve most pages without a syscall. Lower it to trade scan speed
        /// for a smaller per-reader footprint.
        /// </summary>
        public int FileBufferSize { get; set; } = 64 * 1024;

        /// <summary>File access mode. Default: Read.</summary>
        public FileAccess FileAccess { get; set; } = FileAccess.Read;

        /// <summary>
        /// File sharing mode. Default: Read (other processes may read but not write while the database is open).
        /// Set to <see cref="FileShare.ReadWrite"/> when another application (e.g. Microsoft Access) holds a write lock on the file.
        /// </summary>
        public FileShare FileShare { get; set; } = FileShare.ReadWrite;
    }
}
