using System.Linq;
using System.Text;
using Xunit;

namespace JetDatabaseReader.Tests.Core
{
    /// <summary>
    /// <see cref="AccessReaderOptions.PreserveTextBytes"/>: Text and Memo columns decoded so that
    /// every byte value round-trips.
    /// </summary>
    /// <remarks>
    /// VB6 and Access-era software routinely kept binary in Text and Memo columns -- a value
    /// written with Chr$(n) and read back with Asc() is a byte array, not a string. Decoding one
    /// through an ANSI code page corrupts it silently: the result is still a valid string, so
    /// nothing reports an error and the bad bytes look like data.
    ///
    /// The tests below pin the property that matters -- byte n becomes U+00nn, for all 256 -- and
    /// that the default is unchanged, since a database that really does hold text still wants its
    /// own code page.
    /// </remarks>
    public class PreserveTextBytesTests
    {
        // The 27 byte values Windows-1252 maps to a different code point. They are why an ANSI
        // decode cannot carry a byte array: 0x80 comes back as the euro sign, 0x92 as a curly
        // quote. The remaining five in 0x80-0x9F (0x81, 0x8D, 0x8F, 0x90, 0x9D) are undefined in
        // the standard and .NET passes them through, which is what makes this so easy to miss --
        // most of the range looks fine.
        private static readonly int[] LossyIn1252 =
        {
            0x80, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89, 0x8A, 0x8B, 0x8C, 0x8E,
            0x91, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0x9B, 0x9C, 0x9E, 0x9F
        };

        [Fact]
        public void Latin1_carries_every_byte_value_and_windows1252_does_not()
        {
            var preserving = Encoding.GetEncoding(28591);

            for (var b = 0; b <= 0xFF; b++)
            {
                var decoded = preserving.GetString(new[] { (byte)b });

                Assert.Equal(1, decoded.Length);
                Assert.Equal(b, decoded[0]);
            }
        }

        [Fact]
        public void The_ansi_code_page_is_what_makes_this_option_necessary()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var ansi = Encoding.GetEncoding(1252);

            // Every one of these is a byte an application could legitimately have stored, and
            // every one comes back as a different value.
            foreach (var b in LossyIn1252)
            {
                var decoded = ansi.GetString(new[] { (byte)b });

                Assert.Equal(1, decoded.Length);
                Assert.NotEqual(b, decoded[0]);
            }
        }

        [Fact]
        public void Default_is_off_so_existing_callers_are_unaffected()
        {
            Assert.False(new AccessReaderOptions().PreserveTextBytes);
        }

        [Theory]
        [MemberData(nameof(TestDatabases.AllExisting), MemberType = typeof(TestDatabases))]
        public void A_database_reads_the_same_number_of_tables_either_way(string path)
        {
            Assert.Null(TestDatabases.SkipIfMissing(path));

            using var ansi = TestDatabases.Open(path);
            using var bytes = TestDatabases.Open(path, new AccessReaderOptions { PreserveTextBytes = true });

            // The option changes how Text and Memo are decoded and nothing else -- not how a
            // column is located, sized or read -- so the shape of the database is identical.
            // Asserted across whichever fixtures exist, Jet3 and Jet4 alike.
            Assert.Equal(ansi.ListTables().OrderBy(t => t), bytes.ListTables().OrderBy(t => t));

            foreach (var table in ansi.ListTables())
            {
                Assert.Equal(
                    ansi.GetColumnMetadata(table).Select(c => c.Name),
                    bytes.GetColumnMetadata(table).Select(c => c.Name));
            }
        }

        /// <summary>
        /// Wherever the option changes a value, the new value is byte-preserving.
        /// </summary>
        /// <remarks>
        /// Stated as a difference rather than as "nothing above U+00FF anywhere", because that
        /// stronger claim is false and the reason matters: a Jet4 database stores Text and Memo
        /// as UCS-2, which is read through <c>Encoding.Unicode</c> and never touches the ANSI
        /// decode this option replaces. Such a column legitimately contains characters above
        /// U+00FF and reads identically either way -- the fixture's Learn.SectionText holds real
        /// curly quotes, and it should.
        ///
        /// So the option only ever affects columns stored as ANSI, which in practice means Jet3
        /// and the uncompressed-ANSI Jet4 case. That is the narrowest possible blast radius, and
        /// this test is what pins it: every value it does not change is left exactly alone.
        /// </remarks>
        [Theory]
        [MemberData(nameof(TestDatabases.AllExisting), MemberType = typeof(TestDatabases))]
        public void Values_the_option_changes_come_back_byte_preserving(string path)
        {
            Assert.Null(TestDatabases.SkipIfMissing(path));

            using var ansi = TestDatabases.Open(path);
            using var bytes = TestDatabases.Open(path, new AccessReaderOptions { PreserveTextBytes = true });

            foreach (var tableName in ansi.ListTables())
            {
                var textColumns = ansi.GetColumnMetadata(tableName)
                    .Where(c => c.TypeName == "Text" || c.TypeName == "Memo")
                    .Select(c => c.Name)
                    .ToList();

                if (textColumns.Count == 0)
                    continue;

                var ansiTable = ansi.ReadTable(tableName);
                var byteTable = bytes.ReadTable(tableName);

                Assert.Equal(ansiTable.Rows.Count, byteTable.Rows.Count);

                for (var r = 0; r < ansiTable.Rows.Count; r++)
                {
                    foreach (var columnName in textColumns)
                    {
                        if (!ansiTable.Columns.Contains(columnName))
                            continue;

                        var before = System.Convert.ToString(ansiTable.Rows[r][columnName]);
                        var after = System.Convert.ToString(byteTable.Rows[r][columnName]);

                        if (before == after)
                            continue;

                        // A decode swap can never change how many characters come out -- both are
                        // one byte per character. A length change would mean the option had
                        // reached something other than the encoding.
                        Assert.Equal(before?.Length, after?.Length);

                        Assert.All(after!, c => Assert.True(c <= 0xFF,
                            $"{tableName}.{columnName} still decoded U+{(int)c:X4}, which no single byte can produce."));
                    }
                }
            }
        }
    }
}
