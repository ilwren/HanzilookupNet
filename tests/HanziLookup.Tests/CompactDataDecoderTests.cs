using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace HanziLookup.Tests;

/// <summary>Tests for the base64 "compact" decoder of the sub-stroke tables.</summary>
public sealed class CompactDataDecoderTests
{
    [Theory]
    [InlineData("EjRW", new byte[] { 0x12, 0x34, 0x56 })]
    [InlineData("/wCr", new byte[] { 0xff, 0x00, 0xab })]
    [InlineData("QP8S", new byte[] { 0x40, 0xff, 0x12 })]
    public void Known_base64_strings_decode_to_the_expected_bytes(string input, byte[] expected)
    {
        Assert.Equal(expected, CompactDataDecoder.Decode(input));
    }

    [Fact]
    public void Padded_input_is_supported_and_the_padding_positions_read_as_zero()
    {
        // "EjQ=" carries two bytes (base64 padding is one '='), "EjQA" carries three where the last
        // base64 character is index 0 - which is exactly how the reference implementation reads the
        // padding positions of a padded input.
        Assert.Equal(new byte[] { 0x12, 0x34 }, CompactDataDecoder.Decode("EjQ="));
        Assert.Equal(new byte[] { 0x12, 0x34, 0x00 }, CompactDataDecoder.Decode("EjQA"));
    }

    [Fact]
    public void Empty_input_produces_no_bytes()
    {
        Assert.Empty(CompactDataDecoder.Decode(string.Empty));
    }

    [Fact]
    public void The_decoded_length_is_three_quarters_of_the_input()
    {
        var bytes = CompactDataDecoder.Decode("EjRWEJq83v");

        Assert.Equal(9, bytes.Length);
    }

    [Fact]
    public void Decoding_is_stable_for_the_shipped_data()
    {
        var json = File.ReadAllText(TestPaths.Require(TestPaths.DataFile, "The character data"));
        using var document = JsonDocument.Parse(json);
        var base64 = document.RootElement.GetProperty("substrokes").GetString()!;

        var decoded = CompactDataDecoder.Decode(base64);

        Assert.Equal(0, decoded.Length % 3);
        Assert.Equal(469479, decoded.Length);
        Assert.Equal(156493, decoded.Length / 3);
    }

    [Fact]
    public void Characters_outside_the_base64_alphabet_decode_as_zero()
    {
        // The lookup table of the original is a 256 byte array that is zero for every character
        // outside the alphabet, so such characters behave like base64 index 0.
        var decoded = CompactDataDecoder.Decode("EjR\u0001");

        Assert.Equal(new byte[] { 0x12, 0x34, 0x40 }, decoded);
    }

    [Fact]
    public void Unpadded_input_whose_length_is_not_a_multiple_of_four_is_accepted()
    {
        // The reference implementation would throw here (its ArrayBuffer length would be 2.25);
        // this port decodes what it can and ignores the incomplete trailing group.
        var bytes = CompactDataDecoder.Decode("EjR");

        Assert.Equal(new byte[] { 0x12, 0x34 }, bytes);
    }

    [Fact]
    public void Decoding_is_deterministic()
    {
        var first = CompactDataDecoder.Decode("EjRWEJq83vASNFZ4mrze8");
        var second = CompactDataDecoder.Decode("EjRWEJq83vASNFZ4mrze8");

        Assert.Equal(first, second);
        Assert.Equal(15, first.Length); // 21 base64 characters -> 15 whole bytes, the trailing group is ignored
    }

    [Fact]
    public void Null_input_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => CompactDataDecoder.Decode(null!));
    }
}
