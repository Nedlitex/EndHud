# Operator counters

Typhoeus controlled in a boss fight at 4K, from a recording of the owner's own play; everything but
the middle of the HUD band (squad panel edge, counters, skill-point bar) is blacked out, so no UID.
`labels.json` gives, by eye, what the readers must say:

- `huntingArrows` (猎矢): the four slanted bars left of the skill-point bar, one lit per arrow. Lit
  bars are cyan, and with all four a white streak sweeps over them (`12s` to `28s`): still four.
- `signs` (启示): the eight dots right of the bars, lit white one per Sign, filling from the bottom
  row up. `null` with `signsUnreadable` where they can't be told: the flash when the arrows fill
  (`10_5s`, `34_25s`).
- `42_75s`: the HUD sliding away at the end of the fight. The bars are all grey (0), but the dots
  are read at the wrong rows, out of their fill order: `null`.

`zhuang-*.jpg`: Zhuang Fangyi controlled in another of the owner's boss fights (4K, same masking).
`sunderblades` (青霆剑): the diamond grid of nine cells left of the skill-point bar, one lit cyan per
sword; the game prints the count beside it (none at 0), which is what the labels follow. `null`
where the HUD is hidden by an animation (`50s`, `62s`).
