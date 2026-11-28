# Custom category catalog

The «Coming soon!» collection is filled by **categories** — rules the plugin matches films against, not fixed lists. This document explains how to view, export, edit and import them, and how to generate categories for your own library with an LLM.

## Concepts

- A **category** is a rule: which tags, genres, countries, studios or title/description patterns a film must match, plus an optional **date window** (when the category is shown).
- The **built-in catalog** ships inside the plugin. It works out of the box for a typical library.
- A **custom catalog** is a JSON file you import. Its categories **extend** the built-in ones, and a custom category with the same `id` **replaces** the built-in one.
- Categories are matched on every scheduled run, so a new film that fits a category appears in the collection automatically. The catalog only needs to change when you want different categories.

## Where to manage it

Dashboard → Plugins → Jelly Twiggy → **Themed collections** → **Category catalog**:

- **Export JSON** — download the effective catalog (built-in merged with your custom one).
- **Import JSON…** — upload a catalog file. The plugin validates it and shows a report: for each category whether it is valid and **how many films it picks right now**. Nothing is saved if any category is invalid.
- **Roll back** — restore the previous custom catalog (appears after you import).
- **Reset to built-in** — remove the custom catalog (appears while one is in use).
- **Download library profile** — a compact statistical summary of your libraries (genres, tags, studios, countries, directors, decades, sample titles). Give it to an LLM so it proposes categories that actually match your films.

## Catalog format (version 1)

```json
{
  "version": 1,
  "categories": [
    {
      "id": "custom-a24",
      "kind": "holiday",
      "name": "A24 Films",
      "window": { "type": "annual", "month": 10, "day": 31 },
      "rule": {
        "strongTags": ["a24"],
        "weakTags": ["indie film"],
        "titlePattern": "a24",
        "overviewPattern": null,
        "genres": ["Drama", "Thriller"],
        "countries": [],
        "countriesOnly": false,
        "requireStrong": true,
        "threshold": 5
      },
      "limit": 12,
      "minimum": 4,
      "religious": false,
      "season": false
    },
    {
      "id": "custom-ghibli-films",
      "kind": "franchise",
      "franchise": { "studios": ["Studio Ghibli"] }
    }
  ]
}
```

### Category fields

| Field | Required | Meaning |
|---|---|---|
| `id` | yes | Lowercase letters, digits, dashes; up to 50 chars. Custom ids must not clash with built-in ones unless you mean to replace them. |
| `kind` | yes | `holiday` (needs a `rule`) or `franchise` (needs a `franchise`). |
| `name` | custom only | Display name. Built-in categories are named per server language; custom ones use this. |
| `window` | no | When the category shows. Omit for an always-available category (used only when no dated one is due). |
| `rule` | for `holiday` | How a film matches (below). |
| `franchise` | for `franchise` | `collections` (TMDb collection ids), `studios`, `originalTitles` — any that match. |
| `limit` | no | Max films in the collection for this category (default 20). |
| `minimum` | no | Min films for the category to be shown (default 6). |
| `religious` | no | On the same day a religious category yields to non-religious ones (default false). |
| `season` | no | A season shows only when no holiday is due within a month (default false). |

### Window

| `type` | Fields | Meaning |
|---|---|---|
| `annual` | `month`, `day`, `through` | The month before the date up to the date plus `through` days. `through` 0 = the day itself. |
| `fixed` | `startMonth/startDay`, `endMonth/endDay`, `anchorMonth/anchorDay` | A fixed span (a season); `anchor` decides between overlapping windows. |
| `computed` | `dateProvider`, `through` | A movable date from a built-in provider. |

Built-in `dateProvider` names: `orthodoxEaster`, `maslenitsa`, `chineseNewYear`, `thanksgiving`, `friday13`.

### Rule fields

| Field | Match | Weight |
|---|---|---|
| `strongTags` | film tag | 3 |
| `weakTags` | film tag | 1 |
| `genres` | film genre | 3 |
| `countries` | film country | 3 |
| `titlePattern` | regex on title + original title | 4 |
| `overviewPattern` | regex on description | 2 |

A film belongs to the category when its total weight reaches `threshold` (default 5). With `requireStrong: true` it must also match at least one strong tag / genre / country. With `countriesOnly: true` the country list restricts (a film made elsewhere is rejected) instead of scoring. With `requireGenre: true` the film must have one of `genres`.

Regex patterns are limited to 500 chars and compiled with a 1-second timeout. Genre and country names must match your metadata language (the profile shows them).

## Recipe: generate categories with an LLM

1. In the plugin settings, click **Download library profile** — you get `jellytwiggy-profile.json`.
2. Give it to any LLM (ChatGPT, Claude, a local model) with this prompt:

   > You generate film-category rules for a Jellyfin plugin. Reply with **only** JSON of this shape: `{"version":1,"categories":[...]}`. Each category: `id` (starts with `custom-`), `kind` (`holiday` or `franchise`), `name`, optional `window` (`{"type":"annual","month":M,"day":D}` or omitted), `rule` with `strongTags`/`weakTags` (TMDb tags, English), `genres` and `countries` (in my metadata language), `titlePattern`/`overviewPattern` (optional regex), `requireStrong`, `threshold` (default 5), `limit` (default 20), `minimum` (default 6). Propose only categories my library can fill (see the profile: genres, tags, studios, countries, decades, sample titles). Combine signals (studio+decade, country+genre). Do not duplicate plain single genres with hundreds of films. Here is my library profile: `<paste jellytwiggy-profile.json>`

3. Save the reply as `catalog.json`.
4. In the plugin settings click **Import JSON…** and pick the file. Read the report: each category shows how many films it picks. Categories with 0 films will not be shown — fix their rules or remove them.
5. If the report is all valid, the catalog is applied and the collections rebuild.

To refine later: export the catalog, give it to the LLM along with the fresh profile, ask for changes, import again. The previous custom catalog is kept for **Roll back**.

## REST API

All endpoints require an admin API key (Dashboard → API Keys) sent as `X-Emby-Token`. Base URL: `http://your-server:8096`.

```bash
# Library profile (for the LLM)
curl -H "X-Emby-Token: KEY" http://server:8096/JellyTwiggy/Picks/Profile

# Effective catalog
curl -H "X-Emby-Token: KEY" http://server:8096/JellyTwiggy/Picks/Catalog

# Validate without saving (returns per-category film counts)
curl -X POST -H "X-Emby-Token: KEY" -H "Content-Type: application/json" \
     --data @catalog.json http://server:8096/JellyTwiggy/Picks/Catalog/Validate

# Import (validate + save + rebuild)
curl -X POST -H "X-Emby-Token: KEY" -H "Content-Type: application/json" \
     --data @catalog.json http://server:8096/JellyTwiggy/Picks/Catalog

# Roll back to the previous custom catalog
curl -X POST -H "X-Emby-Token: KEY" http://server:8096/JellyTwiggy/Picks/Catalog/Rollback

# Reset to the built-in catalog
curl -X DELETE -H "X-Emby-Token: KEY" http://server:8096/JellyTwiggy/Picks/Catalog
```

A typical script: `Profile → LLM → Validate (check counts) → Catalog`.

## Troubleshooting

- **A category is saved but not shown.** It picked fewer films than its `minimum` (see the import report), it is disabled in the category checkboxes, or «Show» (`EnablePicks`) is off.
- **Regex rejected.** It is longer than 500 chars or does not compile; the report shows the reason.
- **The collection did not change.** Categories are matched on the daily scheduled task; import already triggers a rebuild, otherwise run the «Coming soon!» task from Dashboard → Scheduled Tasks.
- **I broke the catalog.** Use **Roll back**, or **Reset to built-in** to start over.
