using System.Text;
using System.Text.Json.Nodes;
using EndHud.Vision.Inn;
using EndHud.Vision.Operators;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The inn's data (<see cref="InnData"/>, inn/inn-data.json, kept by hand) and the operators'
/// hobbies and elements (from the wiki), as they travel in the operator package.
/// </summary>
public sealed class InnDataTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "endhud-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static string RepositoryInnFile() => Path.Combine(OperatorCache.RepositoryRoot(), InnData.FolderName, InnData.FileName);

    /// <summary>The built-in package, installed into a folder of its own.</summary>
    private async Task<OperatorLibrary> ShippedLibraryAsync()
    {
        string user = Path.Combine(_root, "operators");
        OperatorPackage.Install(Path.Combine(user, OperatorLibrary.ReferenceFolderName), OperatorPackage.BuiltIn()!);
        return await OperatorLibrary.LoadAsync(user);
    }

    [Fact]
    public async Task The_package_carries_the_repositorys_inn_data_and_it_has_no_problems()
    {
        var library = await ShippedLibraryAsync();
        var inn = Assert.IsType<InnData>(library.Inn);

        // Built from the repository's file as it is now (rebuild the package after editing it).
        Assert.Equal(InnData.Parse(File.ReadAllBytes(RepositoryInnFile()))!.ToJson(), inn.ToJson());
        Assert.Empty(inn.Problems());
        Assert.Equal(44, inn.Dishes.Count);
        Assert.Equal(14, inn.Themes.Count);
        Assert.Equal([12, 14, 8, 10], Enum.GetValues<DishType>().Select(t => inn.Dishes.Count(d => d.Type == t)));
    }

    /// <summary>
    /// Every dish and drink has its picture in the package, and EndHud's own image loader reads it
    /// (256-colour PNG): the inn helper shows each name with its picture, and reads orders by them.
    /// </summary>
    [Fact]
    public async Task Every_dish_and_drink_has_a_picture_EndHud_can_read()
    {
        var inn = (await ShippedLibraryAsync()).Inn!;
        string reference = Path.Combine(_root, "operators", OperatorLibrary.ReferenceFolderName);
        var ids = inn.Dishes.Select(d => d.Id).Concat(inn.Drinks.Select(d => d.Id)).ToList();

        foreach (string id in ids)
        {
            var picture = await ImageFile.LoadAsync(InnData.PicturePath(reference, id));
            Assert.Equal((128, 128), (picture.Width, picture.Height));
        }
        Assert.Equal(ids.Count, Directory.GetFiles(Path.Combine(reference, InnData.FolderName, InnData.PictureFolderName)).Length);
    }

    /// <summary>Cards from the game's recipe list (食谱一览), as the owner's screenshots show them.</summary>
    [Theory]
    [InlineData("白瓜肉丸汤", DishType.Soup, 90, 40, "Savory,Rich")]
    [InlineData("茄果鳞肉汤", DishType.Soup, 90, 40, "Sour,Finny")]
    [InlineData("金丝小贝", DishType.Dessert, 80, 35, "Salty,Meaty")]
    [InlineData("八宝饭", DishType.Entree, 80, 35, "Sweet,Lovely")]
    [InlineData("果味沙拉", DishType.Side, 80, 35, "Sour,Veggy")]
    [InlineData("“勺里游”", DishType.Side, 90, 35, "Savory,Sweet")]
    [InlineData("鲜奶炖蛋", DishType.Dessert, 32, 15, "")]
    [InlineData("肉包子", DishType.Entree, 12, 15, "")]
    public async Task Dishes_are_as_the_game_shows_them(string zh, DishType type, int price, int seconds, string traits)
    {
        var inn = (await ShippedLibraryAsync()).Inn!;

        var dish = Assert.Single(inn.Dishes, d => d.Zh == zh);
        Assert.Equal((type, price, seconds), (dish.Type, dish.Price, dish.CookSeconds));
        Assert.Equal(traits.Split(',', StringSplitOptions.RemoveEmptyEntries), dish.Traits);
    }

    /// <summary>
    /// The game's kitchen screen with 秋栗 (Akekuri) and 昼雪 (Snowshine): 主食 +40%, 鲜 +5%, 辣 +5%
    /// ("bonuses of the same kind add up"). Both have two Entree hobbies on the wiki.
    /// </summary>
    [Fact]
    public async Task A_kitchens_bonuses_are_its_chefs_added_up_as_the_game_shows()
    {
        var library = await ShippedLibraryAsync();
        var inn = library.Inn!;

        var bonuses = new[] { "Akekuri", "Snowshine" }.SelectMany(id => inn.ChefBonusesOf(library.Find(id)!)).ToList();

        Assert.Equal(40, bonuses.Where(b => b is { Kind: ChefBonusKind.DishSpeed, DishType: DishType.Entree }).Sum(b => b.Percent));
        Assert.Equal(5, bonuses.Where(b => b is { Kind: ChefBonusKind.Flavor, Flavor: "Savory" }).Sum(b => b.Percent));
        Assert.Equal(5, bonuses.Where(b => b is { Kind: ChefBonusKind.Flavor, Flavor: "Spicy" }).Sum(b => b.Percent));
        Assert.Equal(6, bonuses.Count);
    }

    [Fact]
    public async Task Laevatain_cooks_sides_with_a_better_choice_chance_and_favours_spicy()
    {
        var library = await ShippedLibraryAsync();

        // An article on the event: 莱万汀擅长制作菜肴，炒菜"暴击率"提高5%.
        ChefBonus[] expected = [new(ChefBonusKind.ChoiceChance, 5), new(ChefBonusKind.DishSpeed, 10, DishType.Side), new(ChefBonusKind.Flavor, 5, Flavor: "Spicy")];
        Assert.Equal(expected.OrderBy(b => b.Kind), library.Inn!.ChefBonusesOf(library.Find("Laevatain")!).OrderBy(b => b.Kind));
    }

    [Fact]
    public async Task Si_and_the_Endministrator_cant_cook()
    {
        var library = await ShippedLibraryAsync();

        Assert.Empty(library.Inn!.ChefBonusesOf(library.Find("Si")!));
        Assert.Empty(library.Inn!.ChefBonusesOf(library.Find("Endministrator")!));
    }

    /// <summary>
    /// Every operator who can cook has an element and two hobbies the rules know. Fails when the wiki
    /// adds a hobby or element the inn data doesn't map yet (or an operator's page lacks them).
    /// </summary>
    [Fact]
    public async Task Every_chef_has_an_element_and_two_hobbies_the_rules_know()
    {
        var library = await ShippedLibraryAsync();
        var rules = library.Inn!.ChefBonuses;

        foreach (var op in library.Operators.Where(o => !rules.NotChefs.Contains(o.Id)))
        {
            Assert.True(rules.Elements.Any(e => e.Id == op.Element), $"{op.Id}: element {op.Element ?? "none"}");
            Assert.Equal(2, op.Hobbies.Count);
            Assert.All(op.Hobbies, h => Assert.True(rules.Hobbies.Any(r => r.Id == h), $"{op.Id}: hobby {h}"));
            Assert.Equal(3, library.Inn.ChefBonusesOf(op).Count);
        }
    }

    [Theory]
    [InlineData("|hb1 = Creative\n|hb1d = Creative: Collection of the Weird\n", "hb1", "Creative")]
    [InlineData("|element = Heat\n|faction = Rhodes Island\n", "element", "Heat")]
    [InlineData("|hb1 = ???\n|hb2 = \n", "hb1", null)]
    [InlineData("|hb1 = ???\n|hb2 = \n|cnname = 管理员\n", "hb2", null)]
    [InlineData("|name = Si\n", "hb1", null)]
    public void Infobox_fields_are_read_and_the_wikis_placeholder_is_not_a_value(string content, string field, string? expected)
    {
        Assert.Equal(expected, WikiOperatorSource.InfoboxField(content, field));
    }

    /// <summary>The shipped inn data with <paramref name="change"/> made to its JSON.</summary>
    private static byte[] Edited(Action<JsonObject> change)
    {
        var json = JsonNode.Parse(File.ReadAllText(RepositoryInnFile()))!.AsObject();
        change(json);
        return Encoding.UTF8.GetBytes(json.ToJsonString());
    }

    [Fact]
    public void Problems_name_what_is_wrong()
    {
        var inn = InnData.Parse(Edited(json =>
        {
            var dishes = json["dishes"]!.AsArray();
            dishes[0]!["traits"] = new JsonArray("Smoky");
            dishes[1]!["id"] = dishes[2]!["id"]!.GetValue<string>();
            dishes[3]!["zh"] = "";
            json["themes"]![0]!["prefers"] = new JsonArray("Crunchy");
        }))!;

        Assert.Equal(
            [$"Two dishes are {inn.Dishes[2].Id}.", $"{inn.Dishes[0].Id}: unknown trait Smoky.", $"{inn.Dishes[3].Id}: no Chinese name.", $"Theme {inn.Themes[0].Id}: prefers unknown Crunchy."],
            inn.Problems());
    }

    [Fact]
    public void A_newer_format_is_left_unread_and_something_else_is_refused()
    {
        Assert.Null(InnData.Parse(Edited(json => json["format"] = InnData.CurrentFormat + 1)));
        Assert.Throws<InvalidDataException>(() => InnData.Parse("{\"operators\": []}"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => InnData.Parse("not json"u8.ToArray()));
    }

    /// <summary>A copy of the built-in package dated later, its inn data replaced (null: removed).</summary>
    private byte[] PackageWithInn(byte[]? innJson)
    {
        string folder = Path.Combine(_root, "variant-" + Guid.NewGuid().ToString("N"));
        OperatorPackage.Install(folder, OperatorPackage.BuiltIn()!);
        if (innJson is null) File.Delete(InnData.PathIn(folder));
        else File.WriteAllBytes(InnData.PathIn(folder), innJson);
        return OperatorPackage.Build(folder, DateTime.UtcNow.AddYears(1));
    }

    public static TheoryData<string> BadInn() => ["removed", "broken", "with problems", "most dishes dropped"];

    /// <summary>A newer package with broken or much shorter inn data is refused like a short roster: the current copy stays.</summary>
    [Theory]
    [MemberData(nameof(BadInn))]
    public void A_package_that_would_break_the_inn_data_is_refused(string what)
    {
        string reference = Path.Combine(_root, "reference");
        OperatorPackage.Install(reference, OperatorPackage.BuiltIn()!);
        byte[] before = File.ReadAllBytes(InnData.PathIn(reference));
        byte[]? inn = what switch
        {
            "removed" => null,
            "broken" => "not json"u8.ToArray(),
            "with problems" => Edited(json => json["dishes"]![0]!["traits"] = new JsonArray("Smoky")),
            _ => Edited(json =>
            {
                var dishes = json["dishes"]!.AsArray();
                while (dishes.Count > 10) dishes.RemoveAt(dishes.Count - 1);
            }),
        };

        Assert.Throws<InvalidDataException>(() => OperatorPackage.InstallIfNewer(reference, PackageWithInn(inn)));
        Assert.Equal(before, File.ReadAllBytes(InnData.PathIn(reference)));
    }

    [Fact]
    public async Task A_package_with_inn_data_in_a_newer_format_still_updates_the_operators()
    {
        string user = Path.Combine(_root, "operators");
        string reference = Path.Combine(user, OperatorLibrary.ReferenceFolderName);
        OperatorPackage.Install(reference, OperatorPackage.BuiltIn()!);

        Assert.NotNull(OperatorPackage.InstallIfNewer(reference, PackageWithInn(Edited(json => json["format"] = InnData.CurrentFormat + 1))));

        var library = await OperatorLibrary.LoadAsync(user);
        Assert.NotEmpty(library.Operators);
        Assert.Null(library.Inn);
    }

    [Fact]
    public void A_fix_to_the_inn_data_alone_makes_a_newer_package()
    {
        string folder = Path.Combine(_root, "built");
        OperatorPackage.Install(folder, OperatorPackage.BuiltIn()!);
        byte[] previous = OperatorPackage.Build(folder, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        File.WriteAllBytes(InnData.PathIn(folder), Edited(json => json["dishes"]![0]!["price"] = 13));
        byte[] fixedUp = OperatorPackage.Build(folder, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), previous);

        Assert.True(OperatorPackage.ReadInfo(fixedUp)!.ContentAtUtc > OperatorPackage.ReadInfo(previous)!.ContentAtUtc);
    }
}
