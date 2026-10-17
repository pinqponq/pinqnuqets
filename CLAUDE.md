# Project standards

Shared standards come from **pinq-doq**, vendored under `.pinq-doq/` and copied into `.claude/rules/` (auto-loaded — do not @import the scoped files). Skills are in `.claude/skills/`. Code-gen scripts: `.pinq-doq/scripts/`. Deep references: `.pinq-doq/references/`.

To pull newer standards later, tell Claude: **update rules**.

---

## What belongs in these packages

`pinqnuqets` holds only what any Pinqponq ASP.NET Core API wants for itself. Project-specific things never go in. The full rule is in `.claude/rules/dotnet-conventions.md`, section "Shared Packages (pinqnuqets)".

- For every type, option, default, constant and message ask: would a project other than the first one need exactly this?
- Product-specific headers, query keys, routes, response codes, messages, defaults and one-product options stay in the product. A doubtful part stays in the product, or becomes a setting with a neutral default in an opt-in package.
- Technology-specific packages (PostgreSQL, MongoDB, EF Core, Vault) are fine, if nothing in them names a product.
- When code moves in from a product, strip the product-specific parts first and leave them in the product.
- The pull request lists per package what stayed in, what was left in the product, and why.

