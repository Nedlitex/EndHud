# inn

`inn-data.json` and the dish pictures in `dish-pictures.zip` are the inn of Yuanyuan Manor (the "Yuanyuan's Blazing Stoves" mode, from
version 1.6): its 44 dishes, the traits and 14 themes they're judged by, and how an operator's
hobbies and element help a kitchen. The wiki EndHud takes operators from (endfield.wiki.gg) has no
dish pages yet, so this file is kept by hand. It travels in the operator package
(`operators/operator-library.zip`): editing it on `main` rebuilds the package
(`.github/workflows/operator-package.yml`), and players' EndHud picks the new package up within a
few hours. Read by [`InnData`](../src/EndHud.Vision/Inn/InnData.cs).

The operators' own hobbies and elements aren't here: they come from the wiki with the roster.

## Sources

| What | From |
|---|---|
| Dishes: English names, type, price, cook time, traits, unlock, ingredients; themes: preferences, unlock rating, star scores | [EndfieldHub, Yuanyuan Inn Recipes, Best Chefs and Themes](https://endfieldhub.org/guides/yuanyuan-inn-recipes-chefs-themes), which says it's read from the 1.6 game data (checked there 2026-10-17) |
| Chinese dish names, trait words, 7 theme names | The game's own recipe list (食谱一览) and locked recipes, from the owner's screenshots (2026-10-18). The 32 unlocked dishes match EndfieldHub on type, price, cook time and traits; the 12 locked ones by name and by the theme and star that unlock them. |
| Chef rules: hobby → dish type, element → flavor, "bonuses of the same kind add up" | The game's 干员加成 screen. A kitchen of Akekuri and Snowshine showed 主食 +40%, 鲜 +5%, 辣 +5%, as these rules give from the wiki's hobbies and elements. |
| Dish pictures (`dish-pictures.zip`: `<dish id>.png` each, 128×128, 256 colours) | The game's own dish art, as extracted and published by EndfieldHub (its terms allow sharing its content with credit; the art belongs to the game's developer and publisher), shrunk from 256×256 WebP. Each is the one EndfieldHub labels with that dish's name; three were compared with the game's cards and match. |
| Percentages (+10% speed per hobby, +5% choice chance, +5% flavor from Inn Rating 3); Si and the Endministrator can't cook | EndfieldHub (the 40% above agrees with +10% per hobby) |

Not yet seen in the game, so shown in English until they are: the drink traits (Tea, Kohlic, Milky,
Fruity) and seven theme names (Cereal Galore, Sweet & Salty, Drinking Social, Appetizing Start,
Cereal & Flour, Warming Flow, Cozily Sweet). Never fill one in by guessing; take it from the game.

## Changing it

- **A dish or theme changed in the game:** edit its entry. A dish's `id` stays as it is (it's the
  key), even if a name changes.
- **A new dish:** add an entry with a new `id`, and its picture to `dish-pictures.zip` as `<id>.png`
  (128×128 PNG) when there is one; a dish without a picture still ships, and the build lists it. `traits` are
  trait ids, `unlock` one of `{"rating": n}`, `{"theme": "<theme id>", "stars": n}` or
  `{"other": "<how>"}`.
- **A dish removed from the game:** delete its entry and its picture from the zip. A package that drops more
  than a fifth of the dishes is refused by players' EndHud, as a too-short roster is.
- **A name seen in Chinese for the first time:** fill in its `zh`.

`endhud-cli build-operator-package operators/operator-library.zip --inn inn` refuses a file with
problems (an unknown trait, theme or dish type, two dishes with one id, a dish without a
Chinese name) and says what they are; the tests check the shipped file the same way.
