using EndHud.Vision.Inn;
using EndHud.Vision.Operators;
using Xunit;
using Xunit.Abstractions;
using static EndHud.Tests.InnTestKit;

namespace EndHud.Tests;

/// <summary>
/// The setup panel split by kitchen (owner, 2026-10-19: "too hard to read, split this to each
/// kitchen: in kitchen 1, show suggested cook and suggested dish"). Each kitchen is a block: its
/// heading (厨房一 · its dish types, or 厨房三 · 不用), then 干员 (the planned chefs) and their 可换,
/// 做菜 (the menu dishes it cooks mainly) and their 可换; the drinks last, with theirs. Checked on
/// the view model built from real plans over the whole built-in roster and dish list.
/// </summary>
public class InnSetupBlocksTests(ITestOutputHelper output)
{
    private const string Title = "缘缘庄 · 推荐（没有的干员/菜，换「可换」里的）";

    private static readonly InnPlanner Planner = new(Inn);

    /// <summary>A labelled row: its names, with their pictures.</summary>
    private sealed record Row(string Label, IReadOnlyList<InnSegment> Items);

    private sealed record Block(string Heading, List<Row> Rows);

    /// <summary>The panel's lines after the title, taken apart into blocks and rows by their words.</summary>
    private static List<Block> Blocks(InnStripModel model)
    {
        var blocks = new List<Block>();
        foreach (var line in model.Lines.Skip(1))
        {
            string text = line.Text;
            var items = line.Segments.Where(s => s.PictureId is not null).ToList();
            if (text.StartsWith("厨房", StringComparison.Ordinal)) blocks.Add(new Block(text, []));
            else if (text.StartsWith("饮品：", StringComparison.Ordinal)) blocks.Add(new Block("饮品", [new Row("饮品：", items)]));
            else if (new[] { "干员：", "可换：", "做菜：" }.FirstOrDefault(l => text.StartsWith(l, StringComparison.Ordinal)) is { } label && blocks.Count > 0)
                blocks[^1].Rows.Add(new Row(label, items));
        }
        return blocks;
    }

    /// <summary>The 可换 rows right under each row labelled <paramref name="label"/>.</summary>
    private static List<IReadOnlyList<InnSegment>> After(IEnumerable<Block> blocks, string label) =>
        [.. blocks.SelectMany(b => b.Rows.Zip(b.Rows.Skip(1)).Where(p => p.First.Label == label && p.Second.Label == "可换：").Select(p => p.Second.Items))];

    private static IReadOnlyList<InnSegment> Named(IEnumerable<Block> blocks, string label) =>
        [.. blocks.SelectMany(b => b.Rows).Where(r => r.Label == label).SelectMany(r => r.Items)];

    private static string Id(InnSegment s) => s.PictureId!.StartsWith(InnSetupView.OperatorPicture, StringComparison.Ordinal)
        ? s.PictureId[InnSetupView.OperatorPicture.Length..]
        : s.PictureId;

    private static InnPlanInput Input(int kitchens, int places, IReadOnlyList<string> hot, IReadOnlyList<DishRule> rules, IReadOnlyList<OperatorInfo>? chefs = null) =>
        new(hot, 4, 0, InnState.MaxTables, [.. Enumerable.Range(1, kitchens).Select(n => new PlanKitchen(n, places))],
            chefs ?? [.. Library.Operators.Where(o => !Inn.ChefBonuses.NotChefs.Contains(o.Id))],
            [.. Inn.Dishes.Select(d => new MenuDish(d, null))], [.. Inn.Drinks.Select(d => new MenuDrink(d, null))],
            new MenuRules(6, 8, 3, 8, 3, rules));

    private (InnSetupAdvice Advice, InnStripModel Model) Panel(InnPlanInput input)
    {
        var advice = InnSetupAdvice.For(Planner, input);
        var model = InnSetupView.Build(new InnSetupInputs([], input, "k", []), advice, Inn, Text);
        foreach (var line in model.Lines) output.WriteLine(new string(' ', 2 * line.Indent) + line.Text);
        return (advice, model);
    }

    /// <summary>What every full panel holds, whatever the plan.</summary>
    private static void AssertBlocks(InnPlanInput input, InnPlan plan, InnStripModel model)
    {
        Assert.Equal(Title, model.Lines[0].Text);
        var blocks = Blocks(model);
        var kitchens = blocks.Where(b => b.Heading.StartsWith("厨房", StringComparison.Ordinal)).ToList();
        Assert.Equal(input.Kitchens.Select(k => Text($"Inn.Kitchen.{k.Number}")), kitchens.Select(b => b.Heading.Split(" · ")[0]));
        Assert.Equal("饮品", blocks[^1].Heading);

        foreach (var (block, planned) in kitchens.Zip(plan.Kitchens.OrderBy(k => k.Number)))
        {
            if (planned.Chefs.Count == 0)
            {
                // Not used: its heading says so, and nothing else.
                Assert.Equal($"{Text($"Inn.Kitchen.{planned.Number}")} · 不用", block.Heading);
                Assert.Empty(block.Rows);
                continue;
            }
            Assert.Matches(@"^厨房[一二三]( · (主食|菜肴|汤羹|甜品)(、(主食|菜肴|汤羹|甜品))*)?$", block.Heading);
            // 干员 first: the planned chefs, each with their avatar, as many as the kitchen's places at most.
            Assert.Equal("干员：", block.Rows[0].Label);
            var chefs = block.Rows[0].Items;
            Assert.Equal(planned.Chefs.Select(c => InnSetupView.OperatorPicture + c.Id), chefs.Select(s => s.PictureId));
            Assert.Equal(planned.Chefs.Select(c => c.NameZh), chefs.Select(s => s.Text));
            Assert.True(chefs.Count <= input.Kitchens.Single(k => k.Number == planned.Number).Places);
        }

        // Every menu dish under exactly one kitchen, with its picture and name.
        var cooked = Named(kitchens, "做菜：");
        Assert.Equal(plan.Dishes.Select(d => d.Dish.Id).Order(), cooked.Select(Id).Order());
        Assert.All(cooked, s => Assert.Equal(Inn.Dishes.Single(d => d.Id == s.PictureId).Zh, s.Text));
        // The drinks: the plan's.
        Assert.Equal(plan.Drinks.Select(d => d.Drink.Id), blocks[^1].Rows[0].Items.Select(Id));

        // Substitutes: at most three a row, never anything recommended, never twice on the panel.
        var chefSubs = After(kitchens, "干员：");
        var dishSubs = After(kitchens, "做菜：");
        var drinkSubs = After([blocks[^1]], "饮品：");
        Assert.All(chefSubs.Concat(dishSubs).Concat(drinkSubs), row => Assert.InRange(row.Count, 1, 3));
        var plannedChefs = plan.Kitchens.SelectMany(k => k.Chefs).Select(c => c.Id).ToHashSet();
        var chefSubIds = chefSubs.SelectMany(r => r).Select(Id).ToList();
        Assert.DoesNotContain(chefSubIds, plannedChefs.Contains);
        Assert.Equal(chefSubIds.Count, chefSubIds.Distinct().Count());
        Assert.DoesNotContain(chefSubIds, id => Inn.ChefBonuses.NotChefs.Contains(id));
        var dishSubIds = dishSubs.SelectMany(r => r).Select(Id).ToList();
        Assert.DoesNotContain(dishSubIds, id => plan.Dishes.Any(d => d.Dish.Id == id));
        Assert.Equal(dishSubIds.Count, dishSubIds.Distinct().Count());
        var drinkSubIds = drinkSubs.SelectMany(r => r).Select(Id).ToList();
        Assert.DoesNotContain(drinkSubIds, id => plan.Drinks.Any(d => d.Drink.Id == id));
        Assert.Equal(drinkSubIds.Count, drinkSubIds.Distinct().Count());

        // Words only where they belong: labels, headings, notes and the spaces between names; no tiers, ids or figures.
        Assert.All(model.Lines.Skip(1).SelectMany(l => l.Segments).Where(s => s.PictureId is null),
            s => Assert.True(s.Text.Trim('　', ' ', ' ').Length == 0 || s.Text.EndsWith('：') || s.Text.StartsWith("厨房", StringComparison.Ordinal)
                || s.Text.StartsWith("看不懂", StringComparison.Ordinal) || s.Text.StartsWith("现有的菜", StringComparison.Ordinal), $"\"{s.Text}\" has no picture"));
        Assert.DoesNotContain(model.Lines, l => l.Text.Any(char.IsAsciiLetterOrDigit));
        // Rows sit indented under their heading.
        Assert.All(model.Lines.Where(l => l.Text.StartsWith("干员：", StringComparison.Ordinal) || l.Text.StartsWith("做菜：", StringComparison.Ordinal)),
            l => Assert.Equal(1, l.Indent));
    }

    [Fact]
    public void Truly_wholesome_three_kitchens_of_two()
    {
        var input = Input(3, 2, ["Veggy", "Rich"], [new DishRule(2, ["Veggy", "Rich"])]);
        var (advice, model) = Panel(input);

        AssertBlocks(input, advice.Plan, model);
    }

    /// <summary>The owner's live case: 舌上清流, at least one 汤羹 dish, 热门 汤羹 and 饮品, three kitchens of two.</summary>
    [Fact]
    public void A_soup_theme_three_kitchens_of_two()
    {
        var input = Input(3, 2, ["Soup", InnData.Drink], [new DishRule(1, ["Soup"])]);
        var (advice, model) = Panel(input);

        AssertBlocks(input, advice.Plan, model);
        Assert.Contains(Named(Blocks(model), "做菜："), s => Inn.Dishes.Single(d => d.Id == s.PictureId).Type == DishType.Soup);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 1)]
    public void Fewer_kitchens_and_places(int kitchens, int places)
    {
        var input = Input(kitchens, places, ["Veggy", "Rich"], [new DishRule(2, ["Veggy", "Rich"])]);
        var (advice, model) = Panel(input);

        AssertBlocks(input, advice.Plan, model);
    }

    [Fact]
    public void A_kitchen_without_a_chef_is_not_used()
    {
        // One cook for two kitchens: the second can't cook.
        var cook = Library.Operators.First(o => Inn.ChefBonusesOf(o).Count > 0);
        var input = Input(2, 1, [], [], [cook]);
        var (advice, model) = Panel(input);

        Assert.Empty(advice.Plan.Kitchens.Single(k => k.Number == 2).Chefs);
        AssertBlocks(input, advice.Plan, model);
        Assert.Contains(model.Lines, l => l.Text == "厨房二 · 不用");
    }
}
