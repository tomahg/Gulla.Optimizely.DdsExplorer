# Gulla.Optimizely.DdsExplorer

A **Dynamic Data Store explorer** for **Optimizely CMS 13**. Browse DDS stores, see their definitions, view and edit an item as JSON, and delete items, empty stores or delete stores entirely, from the settings area of the CMS.

## Requirements

- .NET 10
- Optimizely CMS 13.1.1 or later (`EPiServer.CMS.Core` / `EPiServer.CMS.UI.Core`)

## Installation

```
dotnet add package Gulla.Optimizely.DdsExplorer
```

In `Program.cs`:

```csharp
builder.Services.AddDdsExplorer();
```

## Usage

Log in as an administrator, open **Settings**, and pick **DDS Explorer** under **Tools**.

- **Stores** — every store with its item count and number of properties, filterable by name. Stores that look like they belong to Optimizely are badged **System**.
- **A store** — its definition (each property's name, CLR type, mapping kind and whether it can be edited here) and its items, 50 per page. Paging runs in SQL, so large stores are fine.
- **An item** — click its Id to open it as JSON. From there it can be edited or deleted.

### Deleting

| Action | What it does | Confirmation |
|---|---|---|
| Delete item | Deletes one item | Confirm dialog |
| Delete selected | Deletes the items ticked on the current page | Confirm dialog |
| Empty store | Deletes every item, keeps the store and its definition | Type the store name |
| Delete store | Deletes the store, its definition and every item | Type the store name |

**Empty store** is usually the safer choice: the code that owns the store keeps working, because the store does not have to be recreated with its mapping.

System stores get an extra warning but are never blocked. Deleting one can break the site.

### Editing

> ⚠️ **Editing raw DDS data can corrupt it.** The code that owns a store expects its data in a particular shape, and a value that is valid for its type can still break that code. There is no undo.

Saving is strict, and a save that breaks any rule is rejected as a whole:

- The document must have exactly the store definition's properties plus `Id` — none added, none removed. Keys are case-sensitive.
- `Id` cannot be changed.
- Every changed value must convert to the property's declared CLR type. Numbers are JSON numbers; `Guid`, `DateTime` (ISO 8601, e.g. `2026-09-29T12:00:00.0000000Z`), `Identity` and `char` are strings; enums are names (a defined number is accepted too).
- Only **inline** properties of simple types (string, bool, numbers, `Guid`, `DateTime`, `Identity`, enums, and their nullable forms) can be changed. **Collections**, **references** and properties whose type can no longer be loaded are read-only: they may be left as they are, but any change to them is rejected.

The item is saved the way its owner would save it: loaded as its own CLR type, only the changed properties set, then saved. References and collections are left exactly as they were. An item whose CLR type cannot be loaded cannot be edited at all.

### Broken stores

A store whose definition no longer loads is badged **Broken** in the list.

Such a store opens in **raw mode**, read straight from the DDS tables: the definition with its types as stored text, the items from the store's SQL view, and collections and references as their rows in `tblBigTableReference`. Nothing in raw mode can be edited, and neither items nor the store can be emptied, but the store can be **deleted**, which the explorer does by name, without loading the definition.

### Logging

Every edit and delete is logged at **Information** level through `ILogger`, with the user, the store and the item Id(s); an edit also names the properties it changed. No values are logged, neither before nor after the change, so nothing a store holds, personal data included, ends up in the site log. That also means the log cannot restore a bad edit. Take a database backup first if you may need to go back.

If the site's minimum log level is above Information, these entries are dropped. To keep them, lower the level for the add-ons own category:

```json
{
  "Logging": {
    "LogLevel": {
      "Gulla.Optimizely.DdsExplorer": "Information"
    }
  }
}
```

## Configuration

```csharp
builder.Services.AddDdsExplorer(options =>
{
    options.PageSize = 100;
    options.SystemStorePrefixes = ["EPiServer.", "Optimizely.", "MyCompany.Internal."];
});
```

or in `appsettings.json`:

```json
{
  "Gulla": {
    "DdsExplorer": {
      "PageSize": 100,
      "SystemStorePrefixes": ["EPiServer.", "Optimizely.", "MyCompany.Internal."]
    }
  }
}
```

`SystemStorePrefixes` replaces the default list, so include any default prefixes you want to keep.

| Option | Default | Meaning |
|---|---|---|
| `PageSize` | `50` | Items per page, 1–1000. |
| `SystemStorePrefixes` | `EPiServer.`, `Episerver.`, `Optimizely.`, `Mediachase.`, `EPiServer_`, `Episerver_`, `Optimizely_`, `Forms.`, `Find.` | Store name prefixes badged **System** (matched case-insensitively). Setting it replaces the whole list. |

## Authorization

By default the explorer is open to `CmsAdmins`, `Administrators` and `WebAdmins`, through a policy named `DdsExplorerAdmin`. To change it, register your own policy with that name:

```csharp
builder.Services.AddDdsExplorer(authorization =>
{
    authorization.AddPolicy("DdsExplorerAdmin", policy => policy.RequireRole("CmsAdmins"));
});
```

A policy you define yourself always takes precedence over the default, wherever it is registered.
