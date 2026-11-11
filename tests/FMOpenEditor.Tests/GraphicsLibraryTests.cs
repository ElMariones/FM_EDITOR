using System.Diagnostics;
using FMOpenEditor.Core.Graphics;
using FMOpenEditor.Core.Model;
using Xunit.Abstractions;

namespace FMOpenEditor.Tests;

public sealed class GraphicsLibraryTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fmoe-graphics-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string Pack(string relative, string records, params string[] files)
    {
        var dir = Path.Combine(_root, relative.Replace('\\', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "config.xml"),
            $"<record>\n\t<boolean id=\"preload\" value=\"false\"/>\n\t<list id=\"maps\">\n{records}\n\t</list>\n</record>");
        foreach (var f in files)
        {
            var path = Path.Combine(dir, f.Replace('\\', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [1]);
        }
        return dir;
    }

    [Fact]
    public void Finds_faces_and_badges_wherever_the_pack_is_nested()
    {
        var faces = Pack(@"faces\some pack\faces",
            """
            <record from="face_101" to="graphics/pictures/person/101/portrait"/>
            <record from="face_102" to="graphics/pictures/person/102/portrait"/>
            """, "face_101.png", "face_102.jpg");
        var logos = Pack(@"logos\Clubs\Normal\Small",
            """<record from="7" to="graphics/pictures/club/7/icon"/>""", "7.png");
        Pack(@"logos\Clubs\Normal\Normal",
            """<record from="7" to="graphics/pictures/club/7/logo"/>""", "7.png");

        var lib = GraphicsLibrary.Load([_root]);

        Assert.Equal(3, lib.PackCount);
        Assert.Equal(Path.Combine(faces, "face_101.png"), lib.Find(EntityKind.Player, 101, GraphicSize.Small));
        Assert.Equal(Path.Combine(faces, "face_102.jpg"), lib.Find(EntityKind.Staff, 102, GraphicSize.Large));
        Assert.Equal(Path.Combine(logos, "7.png"), lib.Find(EntityKind.Club, 7, GraphicSize.Small));
        Assert.EndsWith(Path.Combine("Normal", "Normal", "7.png"), lib.Find(EntityKind.Club, 7, GraphicSize.Large));
        Assert.True(lib.HasAny(EntityKind.Player));
        Assert.False(lib.HasAny(EntityKind.Nation));
        Assert.Equal(2, lib.CountOf("person"));
    }

    [Fact]
    public void Only_people_clubs_competitions_and_nations_have_pictures()
    {
        var flags = Pack(@"flags", """<record from="5" to="graphics/pictures/nation/5/icon"/>""", "5.png");
        var lib = GraphicsLibrary.Load([_root]);

        Assert.Equal(Path.Combine(flags, "5.png"), lib.Find(EntityKind.Nation, 5, GraphicSize.Small));
        // Currency 5, continent 5, city 5... share the number but are not that nation.
        foreach (var kind in Enum.GetValues<EntityKind>().Where(k => k > EntityKind.Nation))
        {
            Assert.Null(lib.Find(kind, 5, GraphicSize.Small));
            Assert.Null(lib.Find(kind, 5, GraphicSize.Large));
            Assert.False(lib.HasAny(kind));
        }
    }

    [Fact]
    public void Missing_pictures_and_missing_files_return_null()
    {
        Pack("faces", """<record from="face_5" to="graphics/pictures/person/5/portrait"/>""");

        var lib = GraphicsLibrary.Load([_root]);

        Assert.Null(lib.Find(EntityKind.Player, 5, GraphicSize.Small));  // mapped, file not there
        Assert.Null(lib.Find(EntityKind.Player, 6, GraphicSize.Small));  // never mapped
        Assert.Null(lib.Find(EntityKind.Club, 5, GraphicSize.Small));
    }

    [Fact]
    public void Small_falls_back_to_the_other_slot()
    {
        Pack("logos", """<record from="big/9" to="graphics/pictures/comp/9/logo"/>""", @"big\9.png");

        var lib = GraphicsLibrary.Load([_root]);

        Assert.NotNull(lib.Find(EntityKind.Competition, 9, GraphicSize.Small));
    }

    [Fact]
    public void Ignores_comments_defaults_and_odd_file_names()
    {
        var dir = Pack("mixed",
            """
            <!-- <record from="face_1" to="graphics/pictures/person/1/portrait"/> -->
            <!-- a comment
                 <record from="face_2" to="graphics/pictures/person/2/portrait"/>
                 that spans lines -->
            <record from="0" to="graphics/pictures/club/none/logo/style/0"/>
            <record from="custom name" to="graphics/pictures/person/3/portrait"/>
            <record to='graphics/pictures/person/4/portrait' from='face_4'/>
            """, "face_1.png", "face_2.png", "custom name.png", "face_4.png", "0.png");

        var lib = GraphicsLibrary.Load([_root]);

        Assert.Null(lib.Find("person", 1, "portrait"));
        Assert.Null(lib.Find("person", 2, "portrait"));
        Assert.Equal(Path.Combine(dir, "custom name.png"), lib.Find("person", 3, "portrait"));
        Assert.Equal(Path.Combine(dir, "face_4.png"), lib.Find("person", 4, "portrait"));
        Assert.Equal(2, lib.Count);
    }

    [Fact]
    public void A_missing_folder_gives_an_empty_library()
    {
        var lib = GraphicsLibrary.Load([Path.Combine(_root, "nope")]);
        Assert.True(lib.IsEmpty);
        Assert.Empty(lib.Roots);
        Assert.False(lib.HasAny(EntityKind.Player));
    }

    [Fact]
    public void Locator_uses_only_an_existing_override()
    {
        Directory.CreateDirectory(_root);
        Assert.Equal([_root], GraphicsLocator.Find(null, _root));
        Assert.Empty(GraphicsLocator.Find(null, Path.Combine(_root, "nope")));
    }

    /// <summary>
    /// Indexes the real graphics folder on this machine, when there is one. Skipped silently
    /// elsewhere; set FMOE_GRAPHICS to point it at a specific folder.
    /// </summary>
    [Fact]
    public void Indexes_the_installed_graphics_folder()
    {
        var roots = GraphicsLocator.Find(null, Environment.GetEnvironmentVariable("FMOE_GRAPHICS"));
        if (roots.Count == 0) return;

        var clock = Stopwatch.StartNew();
        var lib = GraphicsLibrary.Load(roots);
        output.WriteLine($"{string.Join(", ", roots)}: {lib.Count:N0} pictures from {lib.PackCount} packs in {clock.ElapsedMilliseconds} ms");
        output.WriteLine($"person {lib.CountOf("person"):N0}, club {lib.CountOf("club"):N0}, comp {lib.CountOf("comp"):N0}, nation {lib.CountOf("nation"):N0}");
        Assert.Equal(roots, lib.Roots);
    }
}
