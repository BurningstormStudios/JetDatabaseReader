using Xunit;

namespace JetDatabaseReader.Tests.Core
{
    /// <summary>
    /// A Jet3 row longer than 255 bytes addresses its variable columns through a jump table.
    /// </summary>
    /// <remarks>
    /// Found against a 2003-era Access database whose widest table -- 185 columns, ~560-byte rows
    /// -- returned NUL bytes and stray values for every text column, while every other table in
    /// the same file read perfectly. A single-byte var_table entry cannot reach past 255, so the
    /// offsets were landing in the row's fixed area.
    ///
    /// Tested through the arithmetic rather than a fixture because the databases that show this
    /// are old, wide, and in this case full of real people's credentials. Synthetic rows also let
    /// a test cross a 256 boundary exactly where it matters.
    /// </remarks>
    public class Jet3JumpTableTests
    {
        // Lays out the tail of a Jet3 row: the var_table in reverse column order with EOD as its
        // last entry, the jump table above it, then var_len. Mirrors CrackRow's own layout.
        private static byte[] BuildRow(int rowSize, int[] lowBytes, int[] jumpEntries,
                                       out int varLen, out int varTableStart, out int varLenPos, out int jumpSz)
        {
            varLen = lowBytes.Length - 1;          // last entry is EOD
            jumpSz = jumpEntries.Length;

            var page = new byte[rowSize];

            varLenPos = rowSize - 8;               // anywhere plausible above the table
            varTableStart = varLenPos - jumpSz - varLen;

            for (var v = 0; v <= varLen; v++)
                page[varTableStart + varLen - 1 - v] = (byte)lowBytes[v];

            for (var j = 0; j < jumpSz; j++)
                page[varLenPos - 1 - j] = (byte)jumpEntries[j];

            return page;
        }

        [Fact]
        public void Offsets_past_the_first_256_bytes_gain_a_block_for_each_jump()
        {
            // Three columns then EOD. The jump says column 2 is where the row crosses 256, so the
            // last two offsets are their byte plus 256 -- the case that silently misread before.
            var page = BuildRow(600, new[] { 40, 100, 20, 90 }, new[] { 2 },
                                out var varLen, out var varTableStart, out var varLenPos, out var jumpSz);

            var offsets = AccessReader.DecodeJet3VarOffsets(page, 0, 600, varLen, varTableStart, varLenPos, jumpSz);

            Assert.NotNull(offsets);
            Assert.Equal(new[] { 40, 100, 276, 346 }, offsets);
        }

        [Fact]
        public void Two_jumps_carry_an_offset_into_the_third_block()
        {
            var page = BuildRow(900, new[] { 10, 30, 5, 60 }, new[] { 2, 3 },
                                out var varLen, out var varTableStart, out var varLenPos, out var jumpSz);

            var offsets = AccessReader.DecodeJet3VarOffsets(page, 0, 900, varLen, varTableStart, varLenPos, jumpSz);

            Assert.NotNull(offsets);
            Assert.Equal(new[] { 10, 30, 261, 572 }, offsets);
        }

        [Fact]
        public void Several_jumps_at_the_same_column_all_apply()
        {
            // A variable column longer than 256 bytes puts two jumps on the same index, and both
            // have to count -- taking one would leave the column an entire block short.
            var page = BuildRow(900, new[] { 10, 20, 30 }, new[] { 1, 1 },
                                out var varLen, out var varTableStart, out var varLenPos, out var jumpSz);

            var offsets = AccessReader.DecodeJet3VarOffsets(page, 0, 900, varLen, varTableStart, varLenPos, jumpSz);

            Assert.NotNull(offsets);
            Assert.Equal(new[] { 10, 532, 542 }, offsets);
        }

        [Fact]
        public void Offsets_before_the_first_jump_are_left_alone()
        {
            var page = BuildRow(600, new[] { 12, 34, 56 }, new[] { 2 },
                                out var varLen, out var varTableStart, out var varLenPos, out var jumpSz);

            var offsets = AccessReader.DecodeJet3VarOffsets(page, 0, 600, varLen, varTableStart, varLenPos, jumpSz);

            Assert.NotNull(offsets);
            Assert.Equal(new[] { 12, 34, 312 }, offsets);
        }

        [Fact]
        public void A_var_table_reaching_outside_the_row_is_refused_rather_than_guessed_at()
        {
            var page = new byte[64];

            var offsets = AccessReader.DecodeJet3VarOffsets(page, 0, 64, varLen: 40, varTableStart: -20,
                                                            varLenPos: 56, jumpSz: 1);

            Assert.Null(offsets);
        }
    }
}
