# Balancia

A single-user personal finance ledger. These terms name the concepts the owner and the code share.

## Categories

**Category**:
A label for what a transaction was for. Every category has a name and either no parent or one parent.
_Avoid_: Tag, label

**Top-level category**:
A category with no parent. Only a top-level category may have subcategories.
_Avoid_: Root category, parent category (it is a parent only while it has children)

**Subcategory**:
A category filed under a top-level category. It cannot have children of its own.
_Avoid_: Child category, nested category

**Category path**:
How a category is written and typed: `Name` for a top-level category, `Parent / Child` for a subcategory. At most two levels, and a name never contains `/`.
_Avoid_: Full name, breadcrumb
