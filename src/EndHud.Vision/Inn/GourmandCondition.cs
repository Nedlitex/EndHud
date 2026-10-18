namespace EndHud.Vision.Inn;

/// <summary>
/// What meets a gourmand's request, written as players write it (the NGA quick-reference table):
/// <list type="bullet">
/// <item>a dish type: 主食, 菜肴, 汤羹, 甜品;</item>
/// <item>a trait in brackets: [咸], [水产]; drinks' traits too: [茶香];</item>
/// <item>an ingredient in angle brackets: &lt;面粉&gt; (the plain grade), &lt;优质肉块&gt; (only the
/// higher grade, [A]), &lt;(优质)鳞肉&gt; (either);</item>
/// <item>非 (not), &amp; (and), 或 (or), and parentheses; &amp; binds tighter than 或.</item>
/// </list>
/// A condition naming a drink trait is a drink request; any other is met by dishes only.
/// </summary>
public sealed class GourmandCondition
{
    private readonly Node _root;

    private GourmandCondition(Node root, bool forDrink)
    {
        _root = root;
        ForDrink = forDrink;
    }

    /// <summary>Met by a drink, not a dish.</summary>
    public bool ForDrink { get; }

    /// <exception cref="FormatException">Not a condition, or it names a type, trait or ingredient <paramref name="inn"/> doesn't know.</exception>
    public static GourmandCondition Parse(string text, InnData inn)
    {
        var parser = new Parser(text, inn);
        var root = parser.Or();
        if (!parser.AtEnd) throw new FormatException($"\"{text}\": unexpected \"{text[parser.Position..]}\".");
        return new GourmandCondition(root, parser.NamesDrinkTrait);
    }

    public bool MetBy(Dish dish) => !ForDrink && _root.Holds(dish.Type, dish.Traits, dish.Ingredients);

    public bool MetBy(InnDrink drink) => ForDrink && _root.Holds(null, drink.Traits, []);

    private abstract record Node
    {
        public abstract bool Holds(DishType? type, IReadOnlyList<string> traits, IReadOnlyList<DishIngredient> ingredients);
    }

    private sealed record Not(Node Inner) : Node
    {
        public override bool Holds(DishType? type, IReadOnlyList<string> traits, IReadOnlyList<DishIngredient> ingredients) =>
            !Inner.Holds(type, traits, ingredients);
    }

    private sealed record All(IReadOnlyList<Node> Parts) : Node
    {
        public override bool Holds(DishType? type, IReadOnlyList<string> traits, IReadOnlyList<DishIngredient> ingredients) =>
            Parts.All(p => p.Holds(type, traits, ingredients));
    }

    private sealed record Any(IReadOnlyList<Node> Parts) : Node
    {
        public override bool Holds(DishType? type, IReadOnlyList<string> traits, IReadOnlyList<DishIngredient> ingredients) =>
            Parts.Any(p => p.Holds(type, traits, ingredients));
    }

    private sealed record IsType(DishType Type) : Node
    {
        public override bool Holds(DishType? type, IReadOnlyList<string> traits, IReadOnlyList<DishIngredient> ingredients) => type == Type;
    }

    private sealed record HasTrait(string Trait) : Node
    {
        public override bool Holds(DishType? type, IReadOnlyList<string> traits, IReadOnlyList<DishIngredient> ingredients) => traits.Contains(Trait);
    }

    /// <param name="Quality">True: only the higher grade; false: only the plain one; null: either.</param>
    private sealed record HasIngredient(string Ingredient, bool? Quality) : Node
    {
        public override bool Holds(DishType? type, IReadOnlyList<string> traits, IReadOnlyList<DishIngredient> ingredients) =>
            ingredients.Any(i => i.Id == Ingredient && (Quality is not { } q || i.Quality == q));
    }

    private sealed class Parser(string text, InnData inn)
    {
        private const string QualityEither = "(优质)";
        private const string QualityOnly = "优质";

        public int Position { get; private set; }
        public bool AtEnd => Position >= text.Length;
        public bool NamesDrinkTrait { get; private set; }

        public Node Or()
        {
            var parts = new List<Node> { And() };
            while (Take("或")) parts.Add(And());
            return parts.Count == 1 ? parts[0] : new Any(parts);
        }

        private Node And()
        {
            var parts = new List<Node> { Unary() };
            while (Take("&")) parts.Add(Unary());
            return parts.Count == 1 ? parts[0] : new All(parts);
        }

        private Node Unary() => Take("非") ? new Not(Unary()) : Atom();

        private Node Atom()
        {
            if (Take("("))
            {
                var inner = Or();
                Expect(")");
                return inner;
            }
            if (Take("["))
            {
                string word = Until(']');
                var trait = inn.Traits.FirstOrDefault(t => t.Zh == word) ?? throw Fail($"unknown trait [{word}]");
                if (trait.Drink) NamesDrinkTrait = true;
                return new HasTrait(trait.Id);
            }
            if (Take("<"))
            {
                bool? quality = Take(QualityEither) ? null : Take(QualityOnly) ? true : false;
                string word = Until('>');
                var ingredient = inn.Ingredients.FirstOrDefault(i => i.Zh == word) ?? throw Fail($"unknown ingredient <{word}>");
                return new HasIngredient(ingredient.Id, quality);
            }
            foreach (var type in inn.DishTypes)
                if (Take(type.Zh)) return new IsType(type.Id);
            throw Fail(AtEnd ? "ends too soon" : $"unexpected \"{text[Position..]}\"");
        }

        private bool Take(string token)
        {
            if (string.CompareOrdinal(text, Position, token, 0, token.Length) != 0) return false;
            Position += token.Length;
            return true;
        }

        private void Expect(string token)
        {
            if (!Take(token)) throw Fail($"expected \"{token}\"");
        }

        private string Until(char end)
        {
            int close = text.IndexOf(end, Position);
            if (close < 0) throw Fail($"no closing {end}");
            string word = text[Position..close];
            Position = close + 1;
            return word;
        }

        private FormatException Fail(string what) => new($"\"{text}\": {what}.");
    }
}
