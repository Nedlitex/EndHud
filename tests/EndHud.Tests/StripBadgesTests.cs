using System.Windows.Media;
using System.Windows.Media.Imaging;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// Conditions as the strip's badges (owner, 2026-10-25: "like 连击 icon >= 1, fire icon >= 1, etc? not text"): per kind,
/// the picture (the shared icon rule, <see cref="ConditionIcon"/>; a skill's own icon for a condition about that skill),
/// the comparison and number, a slash for "not", and a sign where two kinds share a picture. Groups give a badge per part,
/// "or" marked; a "not" over a group is pushed down to its parts.
/// </summary>
public class StripBadgesTests
{
    private static readonly string[] Squad = ["Si", "Last_Rite", "Arcane", "Tangtang", "Laevatain"];

    private static BitmapSource Bitmap() => OnSta(() =>
    {
        var bitmap = new WriteableBitmap(2, 2, 96, 96, PixelFormats.Bgra32, null);
        bitmap.Freeze();
        return (BitmapSource)bitmap;
    });

    private static readonly BitmapSource Face = Bitmap(), BattleSkill = Bitmap(), Combo = Bitmap(), Ultimate = Bitmap(), Status = Bitmap(), Step1 = Bitmap(), Step3 = Bitmap();

    private static readonly Lazy<StripBadges.Pictures> Pictures = new(() => new StripBadges.Pictures(RotationStripViewTests.Operators(Squad),
        (_, action) => action switch { StepAction.Combo => Combo, StepAction.Ultimate => Ultimate, _ => BattleSkill }, _ => Face, _ => Status,
        n => n switch { 1 => Step1, 3 => Step3, _ => null }));

    private static List<StripBadge> Of(StateCondition condition, Func<StateCondition, bool?>? holds = null) =>
        [.. StripBadges.Of([condition], holds ?? (_ => true), Pictures.Value)];

    private static StripBadge B(ImageSource? picture, string? value = null, BadgeSign sign = BadgeSign.None, bool not = false, bool or = false, BadgeGlyph? glyph = null) =>
        new(picture, glyph ?? BadgeGlyph.Picture, value, sign, not, Holds: true, or);

    /// <summary>What each kind of <see cref="ConditionWordsTests.EveryKind"/> looks like, in its order.</summary>
    private static readonly StripBadge[][] Expected =
    [
        [B(null, "≤1", glyph: BadgeGlyph.SkillPoints)], // skill points at most 1
        [B(Combo)], // Si's combo ready
        [B(Combo, "≥80%")], // her combo bar
        [B(BattleSkill)], // her battle skill ready
        [B(Ultimate, not: true)], // her ultimate not ready
        [B(Ultimate, "≥90%")], // its charge
        [B(Ultimate, sign: BadgeSign.Running, not: true)], // Laevatain's ultimate not running
        [B(BattleSkill, sign: BadgeSign.Running, not: true)], // Si's battle skill not running
        [B(BattleSkill, sign: BadgeSign.Empowered)], // her battle skill empowered
        [B(Status, "≥1")], // Link at least 1
        [B(CounterGlyphs.For("damage-stacks"), "<3")], // Si's damage stacks below 3: their drawn glyph, not her face
        [B(Status, "≥3")], // 3 Cryo stacks
        [B(Face)], // Last Rite controlled
        [B(null, "≥2", glyph: BadgeGlyph.Other)], // a phase shown at least twice
        [B(null, ">1", glyph: BadgeGlyph.Other)], // a phase played more than once
        [B(Combo), B(Ultimate)], // and: a badge each
        [B(Combo), B(Ultimate, or: true)], // or: the second after a "/"
        [B(Face, not: true)], // not: Si controlled
        [B(Ultimate, "≤30%", BadgeSign.Running)], // Laevatain's ultimate time left
        [B(null, glyph: BadgeGlyph.Other)], // this phase played through
        [B(null, glyph: BadgeGlyph.Steps)], // every step lit
        [B(null, "≥3", glyph: BadgeGlyph.Steps)], // at least 3 steps lit
        [B(Step1), B(Step3)], // steps 1 and 3 lit: their pictures
        [B(StaggerGlyphs.Bar, "≥50%")], // the stagger bar filled at least half: a drawn bar
        [B(StaggerGlyphs.Node, "≥1")], // at least one node reached: a drawn node box
        [B(StaggerGlyphs.Countdown)], // staggered: the yellow countdown bar
        [B(StaggerGlyphs.Countdown, "≤30%", BadgeSign.Running)], // its time left
    ];

    [Fact]
    public void Every_kind_has_its_badge_and_no_words()
    {
        Assert.Equal(ConditionWordsTests.EveryKind.Length, Expected.Length);
        for (int i = 0; i < Expected.Length; i++)
            Assert.True(Expected[i].SequenceEqual(Of(ConditionWordsTests.EveryKind[i])), $"{ConditionWordsTests.EveryKind[i]}: {string.Join(", ", Of(ConditionWordsTests.EveryKind[i]))}");
        // Only comparisons, numbers and roman numerals: nothing a player has to read as a word.
        foreach (var badge in ConditionWordsTests.EveryKind.SelectMany(c => Of(c)))
            Assert.Matches("^[≥≤=<>][0-9IVX.]+%?$", badge.Value ?? "=0");
    }

    [Fact]
    public void A_status_present_shows_its_stacks_and_absent_a_slash()
    {
        Assert.Equal([B(Status, "≥1")], Of(new BossStatusCondition("Heat Infliction", true)));
        Assert.Equal([B(Status, not: true)], Of(new BossStatusCondition("Heat Infliction", false)));
        // Not absent: present, at least 1.
        Assert.Equal([B(Status, "≥1")], Of(new NotCondition(new BossStatusCondition("Heat Infliction", false))));
        // A reaction's level in the game's numerals (as the words say it).
        Assert.Equal([B(Status, "≥II")], Of(new BossStatusCondition("Corrosion", true, 2)));
    }

    [Fact]
    public void Skill_points_show_their_glyph_and_the_number_as_the_game_counts_them()
    {
        Assert.Equal([B(null, "≥1.5", glyph: BadgeGlyph.SkillPoints)], Of(new SkillPointsCondition(Comparison.AtLeast, 1.5)));
        Assert.Equal([B(null, "<2", not: true, glyph: BadgeGlyph.SkillPoints)], Of(new NotCondition(new SkillPointsCondition(Comparison.Below, 2))));
    }

    /// <summary>A "not" over a group is pushed down to its parts: not (a and b) is (not a) or (not b); not (a or b) is (not a) and (not b).</summary>
    [Fact]
    public void Not_over_a_group_is_a_slashed_badge_per_part()
    {
        var a = new ComboCondition("Arcane", true);
        var b = new UltimateCondition("Tangtang", true);

        Assert.Equal([B(Combo, not: true), B(Ultimate, not: true, or: true)], Of(new NotCondition(new AllCondition([a, b]))));
        Assert.Equal([B(Combo, not: true), B(Ultimate, not: true)], Of(new NotCondition(new AnyCondition([a, b]))));
        Assert.Equal([B(Combo)], Of(new NotCondition(new NotCondition(a))));
    }

    /// <summary>Each badge is bright by its own condition (a "not" asked as the "not"): parts of an "or" each by their own.</summary>
    [Fact]
    public void Each_badge_is_bright_by_its_own_condition()
    {
        var a = new ComboCondition("Arcane", true);
        var b = new UltimateCondition("Tangtang", true);
        var asked = new List<StateCondition>();
        bool? Holds(StateCondition c)
        {
            asked.Add(c);
            return c switch { ComboCondition => true, NotCondition { Of: UltimateCondition } => true, UltimateCondition => false, _ => null };
        }

        Assert.Equal([true, false], Of(new AnyCondition([a, b]), Holds).Select(x => x.Holds));
        Assert.Equal([true], Of(new NotCondition(b), Holds).Select(x => x.Holds));
        Assert.Equal([false], Of(new SkillPointsCondition(Comparison.AtLeast, 1), Holds).Select(x => x.Holds)); // unknown: dim
        Assert.Contains(asked, c => c is NotCondition { Of: UltimateCondition });

        // "Step 2 lit" in a finish condition: bright by its own check (only the follower knows this round's lights).
        var stepTwo = new StepsLitCondition(StepsLitWhich.These, Steps: [2]);
        Assert.Equal([true], Of(stepTwo, c => ReferenceEquals(c, stepTwo) ? true : null).Select(x => x.Holds));
    }

    /// <summary>A step's own ability: only a battle skill short of skill points gets a badge, the skill points it needs, dim.</summary>
    [Fact]
    public void Only_a_battle_skill_short_of_skill_points_gets_a_badge_for_its_own_ability()
    {
        var battleSkill = new RotationStep("Si", StepAction.BattleSkill);
        var notReady = new ConditionCheck(CheckKind.OwnAbility, null, false);
        Vision.PartyState Points(double? points) => new([]) { SkillPoints = points };

        Assert.Equal(new StripBadge(null, BadgeGlyph.SkillPoints, "≥1", BadgeSign.None, Not: false, Holds: false), StripBadges.OwnAbility(battleSkill, notReady, Points(0.96)));
        Assert.Null(StripBadges.OwnAbility(battleSkill, notReady, Points(1.2))); // cooling
        Assert.Null(StripBadges.OwnAbility(battleSkill, notReady, Points(null))); // not read
        Assert.Null(StripBadges.OwnAbility(battleSkill, new ConditionCheck(CheckKind.OwnAbility, null, true), Points(0.5)));
        Assert.Null(StripBadges.OwnAbility(new RotationStep("Si", StepAction.Combo), notReady, Points(0.5)));
        Assert.Null(StripBadges.OwnAbility(new RotationStep("Si", StepAction.Ultimate), notReady, Points(0.5)));
    }

    private static T OnSta<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
        return result;
    }
}
