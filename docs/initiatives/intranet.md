# Intranet module (shipped)

**Originally captured as "Knowledge Base":** 2026-05-13 (`feature/knowledge-base`)  
**Implemented as:** Intranet module — `feature/intranet-favorite-branch` and related work merged toward `development`  
**Status:** Shipped (v1). See `My.Client/Pages/Intranet/*`, `IntranetFunction.cs`, `Intranet*` entities/DTOs, `CuratedNavItem.razor`, `IntranetFileHelper.cs`.

The original initiative doc called this a "knowledge base." The product name in the app is **Intranet** (`User:Intranet`, `Editor:Intranet`, `Admin:Intranet`). The two terms refer to the same module.

---

## What was built (v1)

### Pages and content

- **Page model** (`IntranetPage`): title, optional slug, parent/child hierarchy, `SortOrder`, `IsPublished` (draft vs live), HTML body in `ContentMarkdown` (historical field name), optional `RestrictEditingToOwner` and `Visibility` on the entity for future tightening.
- **Viewing** (`/intranet/pages/{id|slug}`): rendered HTML, draft banner for editors, sub-page links, attachment list, star-to-favorite, **Edit this page** for `Editor:Intranet+`.
- **Editing** (`/intranet/editor`): Quill WYSIWYG (lazy-loaded from `wwwroot/lib/quill/` via `quill-editor.js`). Tabs: **Content**, **Attachments** (used/unused Drive files), **Page Settings** (slug, parent, menu placement).
- **Editor toolbar**: standard formatting; **image** and **folder** open the Drive insert dialog (upload or library pick); **code** opens full-page HTML view/edit/copy; **link** routes to the insert-link dialog (not Quill's default link tooltip).
- **Private Drive images**: inserted with `data-drive-file-id`; hydrated in editor and page view via `intranet-media.js` + authenticated `/intranet/documents/drive/{id}/media`.
- **Save flow**: **Save**, **Save & view**, and **View page** (no save). Opening the editor from a page passes `fromView=1` so returning to read-only view is one click.

### Navigation (two layers)

1. **Curated menu** (`IntranetNavigationItem`) — what most users see in the sidebar. Built by `Admin:Intranet` on `/intranet/navigation`. Supports internal page links, external URLs, arbitrary depth, reorder, hide (`IsVisible`). Draft-linked items are suppressed for regular users.
2. **Page hierarchy** — parent/child relationships among pages (Manage Pages). A page can exist in the hierarchy without appearing in the curated menu until placed there.

**Sidebar UX** (`NavMenu.razor` + `CuratedNavItem.razor` + `SidebarNavAccordionState`): recursive tree, accordion expand/collapse, active-route highlight, depth indentation. Nav items that link to a page **and** have children: title navigates, chevron toggles sub-pages.

Creation is **contextual from the navigation tree** (add sub-item, add page under, add content). Linking uses **title-based searchable pickers**, not raw IDs.

### Documents library (Google Drive)

- Company folder ID in **App Settings → Intranet**.
- **Documents Library** (`/intranet/documents`) for `Editor:Intranet+`: browse, upload, register, edit metadata, delete/purge, "used on" tracking.
- Editor insert dialog: images, file links, web URLs, upload, create new Google Doc/Sheet/Slides.
- `IntranetFileHelper` centralizes MIME classification, upload naming, insert HTML, and content-reference detection.

### Favorites

- Users with Intranet access can star pages on the page view; favorites appear on the **dashboard** as quick links (`FavoriteIntranetPageIds` in user settings).

### Search (header)

- **App-bar search** (magnifying glass or `/` shortcut): overlay dialog ported from MyWorkspace.Site.Public pattern — not in the left nav.
- `GET /api/intranet/pages/search?q=…` searches title, slug, and HTML body (AND across terms). Editors see drafts; regular users see published pages only.
- `IntranetSearchHelper` in `My.Shared` handles scoring and excerpts; `HeaderSearch` + `HeaderSearchDialog` in `My.Client/Components/Search/`.

### Roles and scoping

- `User:Intranet` — view published pages and curated nav.
- `Editor:Intranet` — pages, editor, documents library.
- `Admin:Intranet` — navigation tree + everything editors can do.
- Strict module scoping (same model as Tyme). Global `Admin` alone sees no Intranet nav and is denied at `ScopedAuthorizeView ScopedOnly="true"`. Use impersonation to test.

### Performance choices

- Quill CSS/JS loaded on demand when the editor page opens (`quillEditor.ensureLoaded`).
- Page bodies fetched per page from the API, not bundled into the WASM payload.
- Curated nav tree loaded for the sidebar (recursive API); not a full sitemap of all page HTML.

---

## Deferred from the original plan (not v1)

| Item | Notes |
|------|--------|
| Version history / revert | No page revisions table |
| Full-text search | Manage Pages has title filter only |
| Block-based / Notion-style editor | Quill HTML editor instead |
| Forms builder | Not started |
| Page templates | Not started |
| Per-page / department ACLs | Module roles only; `Visibility` reserved |
| Markdown-first storage | HTML in `ContentMarkdown` |
| Server-side HTML preview endpoint | Client renders `MarkupString` |

---

## Decisions log

| Date | Decision | Why |
|------|----------|-----|
| 2026-05 | Module named **Intranet**, scoped roles like Tyme | Clear nav group and permission model |
| 2026-06 | **HTML** stored in `ContentMarkdown` | Simple viewer (`MarkupString`); field name kept for migration compatibility |
| 2026-06 | **Quill** replaced prototype `contenteditable` + `wysiwyg.js` | Richer toolbar, stable selection, custom handlers for Drive inserts |
| 2026-06 | **Dual model**: curated nav tree + page hierarchy | Admins control sidebar; editors can organize content without exposing everything |
| 2026-06 | **Google Drive** as attachment store | Reuses existing OAuth; private images via authenticated media proxy |
| 2026-06 | **Favorites** on user settings + dashboard | Personal quick access without changing curated nav |
| 2026-06 | Sidebar accordion in `SidebarNavAccordionState` (tested) | Expand/collapse and active state too fragile in Razor alone |

---

## Key files

| Area | Location |
|------|----------|
| API | `My.AzureFunction/Functions/IntranetFunction.cs` |
| Entities | `My.DAL/Models/IntranetPage.cs`, `IntranetNavigationItem.cs`, `IntranetDocument.cs` |
| Editor | `My.Client/Pages/Intranet/IntranetEditor.razor`, `wwwroot/js/quill-editor.js` |
| View | `My.Client/Pages/Intranet/IntranetPage.razor` |
| Nav admin | `My.Client/Pages/Intranet/IntranetNavigation.razor` |
| Sidebar | `My.Client/Layout/NavMenu.razor`, `My.Client/Components/Layout/CuratedNavItem.razor` |
| Accordion state | `My.Shared/Navigation/SidebarNavAccordionState.cs` |
| File helpers | `My.Shared/IntranetFileHelper.cs`, `IntranetPageUrlHelper.cs` |
| Media hydration | `My.Client/Services/IntranetMediaService.cs`, `wwwroot/js/intranet-media.js` |
| Tests | `My.Tests/Navigation/SidebarNavAccordionStateTests.cs`, `My.Tests/Rules/Intranet*Tests.cs` |

---

## Original planning notes (historical)

<details>
<summary>2026-05 knowledge-base initiative — goal, open questions, and ideas</summary>

**Goal:** Internal mini-site for employees — pages and structured content, Blazor WASM–friendly (lazy editor, server-fetched bodies).

**Initial scope thinking:** page model, WYSIWYG, versioning, search. Out of scope: external publishing, real-time co-editing.

**Open questions (mostly resolved by v1):** authoring model → Quill HTML; attachments → Google Drive; permissions → scoped module roles; who can author → Editor/Admin:Intranet.

**Ideas considered:** Markdown-first storage, server-rendered preview, lazy-loaded editor RCL, Azure AI Search later. v1 chose HTML + client render + JS-lazy Quill.

</details>