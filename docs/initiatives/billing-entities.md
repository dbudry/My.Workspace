# Billing Entities

**Branch:** `feature/billing-entities`
**Captured:** 2026-05-13
**Status:** Captured, not started

## Goal

Add a billing entity / billing contact concept to the system. Billing entities are created for an organization (and possibly for a department — TBD). On top of that, per project or per organization we need the ability to attach purchase order(s) and/or other billing-related documents.

This effort implies a larger architectural shift: **Organizations, Departments, and Contacts become part of a more global scope**, with **Billing** as its own area and **Tyme** as its own area — both consuming the shared organization data.

## Scope notes

- **In scope (so far):**
  - Billing entity / billing contact entity model, attached to organization.
  - Purchase orders and billing documents — attachable per project and/or per organization.
  - Refactoring Organizations / Departments / Contacts to live in a shared/global layer that both Billing and Tyme draw from.
- **Out of scope (so far):** invoicing, AR, payment processing — not yet discussed.
- **TBD:**
  - Whether billing entities can also be scoped to a department, or organization-only.
  - Storage strategy for documents (Azure Blob vs. SQL FILESTREAM vs. ...).
  - Permission model — who in an org can manage billing entities / POs.

## Open questions

- Can a billing entity be associated to a department, or only to an organization?
- What metadata does a purchase order need (number, amount, valid-through dates, attached file, link to project, link to organization)?
- One PO can cover multiple projects? Multiple POs per project?
- What document types beyond POs? (Contracts, SOWs, NDAs?)
- Where do we store the documents — Azure Blob via the Functions API, or somewhere else?
- Who can see / edit billing data? Same role model as Tyme, or new roles?

## Concerns / risks

- This is the first effort that introduces a true **shared / global scope** above the existing Tyme-only model. Getting the boundary right between "shared org data" and "Billing-specific" / "Tyme-specific" matters — a wrong cut here costs a lot to undo.
- The Roles & Scopes pattern (Admin:Tyme etc.) will need an Admin:Billing variant — keep in line with the dynamic role enumeration rule (don't hardcode the trio).
- Document storage adds cost and backup considerations we don't have today.

## Sub-tasks

- [ ] Lock down whether billing entities are org-only or org+department.
- [ ] Draft entity model (BillingEntity, PurchaseOrder, BillingDocument).
- [ ] Decide document storage backend.
- [ ] Define Admin:Billing role scope and any view/manage permissions.
- [ ] Plan the refactor that elevates Organizations/Departments/Contacts to shared scope (separate sub-effort — likely its own PR before any billing UI).
- [ ] UI: where billing lives in the nav, what pages, what mobile story.

## Decisions log

_None yet._

## Pending input from Derek

> "More to come" — Derek flagged additional details will follow. Don't finalize the model until he weighs in again.
