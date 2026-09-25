# CRM pipeline

**Branch:** `feature/port-crm-and-fixes-1.3.31-34`  
**Captured:** 2026-09-25  
**Status:** Ported from ProfitPoint.My 1.3.33–1.3.34

## Goal

A role-gated deal pipeline with a per-deal activity log. Independent of Tyme projects and Organizations management.

## Decisions

- New scope: `User:Crm` / `Editor:Crm` / `Manager:Crm` / `UserAccess:Crm`. Nobody is seeded with CRM roles; an admin assigns them.
- Nav and API stay hidden (403) until a CRM role is present. Global Admin does not pass scoped CRM gates alone.
- Opportunities (deals) have name, fixed stage, amount, expected close, organization, contact, and note. Stages are fixed (Lead through Won/Lost).
- Activities are note, call, meeting, or task on an opportunity only.
- Deals are **not** Tyme projects. A won deal does not become a project.
- Organization notes and CRM notes/activity bodies are capped at 500 characters.
- ContactId on Opportunity is stored without an FK; delete paths clear links via `CrmReferenceCleanup` before contact/org delete.
