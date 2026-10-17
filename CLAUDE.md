# Project standards

Shared standards come from **pinq-doq**, vendored under `.pinq-doq/` and copied into `.claude/rules/` (auto-loaded — do not @import the scoped files). Skills are in `.claude/skills/`. Code-gen scripts: `.pinq-doq/scripts/`. Deep references: `.pinq-doq/references/`.

To pull newer standards later, tell Claude: **update rules**.

---

## What belongs in these packages

`pinqnuqets` holds only code that every Pinqponq ASP.NET Core API needs in the same form. A product-specific thing never goes into it. The full rule is in `.claude/rules/dotnet-conventions.md`, section "Shared Packages (pinqnuqets)".

- Test every type, option, default, constant and message: would a product other than the one this code came from need exactly this? If the answer depends on one product, it stays in that product.
- Product-specific means: a header, query key, route, response code, message or default taken from one product's client or contract, or an option that exists for one product only.
- If you are not sure a part is shared, leave it in the product. You may instead move it into its own opt-in package as a setting that is off by default, with a default that was not copied from a product.
- Technology-specific packages (PostgreSQL, MongoDB, EF Core, Vault) are fine, as long as nothing in them names a product.
- When moving code out of a product into these packages, remove the product-specific parts first. The product keeps them as its own code on top of the package.
- The pull request lists, for each package, what the package kept, what was left in the product, and why.

