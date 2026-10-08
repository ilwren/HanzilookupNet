using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace HanziLookup.Tests;

/// <summary>Tests for loading, parsing and querying character data.</summary>
public sealed class HanziDataTests
{
    /// <summary>A tiny repository: one character, one sub-stroke with direction 64, length 255 and centre (1, 2).</summary>
    private const string SmallJson = """
        {
          "chars": [
            ["一", 1, 1, 0],
            ["人", 2, 3, 3]
          ],
          "substrokes": "QP8S////"
        }
        """;

    [Fact]
    public void A_repository_can_be_parsed_from_a_json_string()
    {
        var data = HanziData.Parse(SmallJson);

        Assert.Equal(2, data.Count);
        Assert.Equal("一", data.Characters[0].Character);
        Assert.Equal(1, data.Characters[0].StrokeCount);
        Assert.Equal(1, data.Characters[0].SubStrokeCount);
        Assert.Equal(0, data.Characters[0].SubStrokeOffset);
        Assert.Equal(6, data.SubStrokes.Length);
    }

    [Fact]
    public void Sub_stroke_bytes_can_be_read_per_character()
    {
        var data = HanziData.Parse(SmallJson);

        var first = data.Find("一");
        Assert.NotNull(first);
        var bytes = data.GetSubStrokeBytes(first.Value).ToArray();
        Assert.Equal(3, bytes.Length);
        Assert.Equal(0x40, bytes[0]);
        Assert.Equal(0xff, bytes[1]);

        var second = data.Find("人");
        Assert.NotNull(second);
        var secondBytes = data.GetSubStrokeBytes(second.Value).ToArray();
        Assert.Equal(3, secondBytes.Length);
        Assert.Equal(new byte[] { 0xff, 0xff, 0xff }, secondBytes);
    }

    [Fact]
    public void Missing_characters_are_not_found()
    {
        var data = HanziData.Parse(SmallJson);

        Assert.Null(data.Find("無"));
        Assert.Null(data.Find(""));
    }

    [Fact]
    public void Invalid_json_is_rejected_with_a_useful_message()
    {
        Assert.Throws<FormatException>(() => HanziData.Parse("{\"substrokes\":\"\"}"));
        Assert.Throws<FormatException>(() => HanziData.Parse("{\"chars\":[[\"一\",1]]}"));
        Assert.Throws<System.Text.Json.JsonException>(() => HanziData.Parse("not json at all"));
    }

    [Fact]
    public async Task A_repository_can_be_loaded_from_a_file_and_from_a_stream()
    {
        var path = TestPaths.Require(TestPaths.DataFile, "The character data");

        var fromFile = HanziData.Load(path);
        Assert.Equal(9507, fromFile.Count);

        await using var stream = File.OpenRead(path);
        var fromStream = await HanziData.LoadAsync(stream);
        Assert.Equal(fromFile.Count, fromStream.Count);
        Assert.Equal(fromFile.SubStrokes.Length, fromStream.SubStrokes.Length);
    }

    [Fact]
    public async Task A_repository_can_be_loaded_asynchronously()
    {
        var path = TestPaths.Require(TestPaths.DataFile, "The character data");

        var data = await HanziData.LoadAsync(path);

        Assert.Equal(9507, data.Count);
    }

    [Fact]
    public void The_shipped_data_contains_the_expected_characters()
    {
        var data = HanziData.Load(TestPaths.Require(TestPaths.DataFile, "The character data"));

        var one = data.Find("一");
        Assert.NotNull(one);
        Assert.Equal(1, one.Value.StrokeCount);

        var first = data.Characters[0];
        Assert.Equal("丿", first.Character);
        Assert.Equal(1, first.StrokeCount);

        var last = data.Characters[data.Count - 1];
        Assert.Equal("鱻", last.Character);
        Assert.Equal(33, last.StrokeCount);
        Assert.Equal(43, last.SubStrokeCount);
    }

    [Fact]
    public void Registered_repositories_can_be_replaced_and_removed()
    {
        var first = HanziData.Parse(SmallJson);
        var second = HanziData.Parse(SmallJson);

        HanziDataStore.Register("test-repo", first);
        Assert.Same(first, HanziDataStore.Get("test-repo"));

        HanziDataStore.Register("test-repo", second);
        Assert.Same(second, HanziDataStore.Get("test-repo"));

        Assert.True(HanziDataStore.Remove("test-repo"));
        Assert.False(HanziDataStore.TryGet("test-repo", out _));
        Assert.Throws<KeyNotFoundException>(() => HanziDataStore.Get("test-repo"));
    }

    [Fact]
    public void HanziCharacter_entries_expose_their_fields()
    {
        var character = new HanziCharacter("好", 6, 10, 14358);

        Assert.Equal("好", character.Character);
        Assert.Equal(6, character.StrokeCount);
        Assert.Equal(10, character.SubStrokeCount);
        Assert.Equal(14358, character.SubStrokeOffset);
        Assert.Contains("好", character.ToString());
    }

    [Fact]
    public void Utf8_json_is_supported()
    {
        var bytes = Encoding.UTF8.GetBytes(SmallJson);
        var data = HanziData.Parse(bytes.AsMemory());

        Assert.Equal(2, data.Count);
        Assert.Equal("一", data.Characters[0].Character);
    }
}
