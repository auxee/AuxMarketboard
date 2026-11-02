# AuxMarketboard

A Dalamud plugin that prices a shopping list with live Universalis data and works out where to buy it.

Open it with `/mmb`.

## What it does

- Keeps a shopping list of items with a quantity and an optional HQ-only flag. The list is saved and restored between sessions.
- Search only offers marketable items. Pressing Enter in the search adds the top match.
- Imports Teamcraft links, Artisan JSON exports and plain lines such as `Item x3`, `3x Item`, `Item: 3`, `Item,3` and `12345,3`. Imported quantities add onto what is already in the list, and anything that cannot be resolved or cannot be sold on the market is skipped and counted.
- Prices come from your home world: pick World, Data Center or Region and the plugin looks up the matching scope from Universalis. Log in once so it knows your home world; the last known world is remembered.
- Every listing is a whole stack, so totals are worked out by choosing the cheapest combination of stacks that covers the quantity you need, including the 5% market tax. Overbuy and shortfalls are shown.
- The Buy plan tab groups purchases by world, with the home data center first. Setting Max worlds limits how many worlds the plan may use and shows how much more that costs than the cheapest unrestricted plan.
- Prices refresh when you press Refresh, change the list or change the scope. There is no timed refresh.

## Data and limits

- Prices are fetched from the [Universalis API](https://docs.universalis.app) in chunks of up to 100 items, with at most four requests in flight. Rate limits are respected and failed requests are retried.
- Worlds, data centers and regions come from Universalis rather than a built-in table.
