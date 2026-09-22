# Expenses module

**Branch:** `feat/#1070_expenses-module`  
**Captured:** 2026-09-01  
**Status:** In progress (v1 largely shipped, still on branch)

## Goal

Replace the monthly Excel Form 87-43 + Drive PDF working folder (`Expenses/Working/{yyyy_MM}`) with an in-app **Expenses** module. Employees submit reimbursable costs against **the home company** (the company), not Tyme client/project time. Receipts live in a **private** Google Drive folder gated by the app.

## Decisions

- New scope: `User:Expenses` / `Manager:Expenses` / `UserAccess:Expenses`. No `Editor:Expenses` and no `Admin:Expenses`. Not nested under Tyme.
- v1 workflow: **Draft → Submit** (lock). No in-app approve/reimbursed yet. Manager+ can view team and unsubmit.
- Employee entry is **month + line items** (date, description, category, amount, mileage, meals B/L/D). Not Form 87-43 header fields (department, mail-to, purpose, plant, charge-to, cover dates) — those are blank or constant on real reports.
- Charge-to is **not** a Tyme project. Home company is the home company (`HomeOrganizationId` App Setting — Organizations has no “this is us” flag today).
- Receipts: Drive via a **privileged owner token**, not each employee’s token. Folder is not company-shared (unlike Intranet). Layout `{root}/{LastName}_{FirstName}/{yyyy}_{MM}/{yyyy_MM_dd}_{Slug}.ext`. Submitted statement PDFs are filed under `{root}/Filed/{yyyy}_{MM}/` (outside the person folder) so they survive user-delete. Unsubmit removes the filed PDF; the next submit writes a new one.
- Accounting later: **QuickBooks**. v1 does not integrate. Keep closed category enum + SQL money so a Data page (`/expenses/data`) and QBO push can land without remodeling.

## What is in this slice

- Scope, assignable roles, `AuthGates.RequireScopedExpenses`, role seed.
- Nav group + **My Expenses** list (`/expenses`) and report editor (`/expenses/{id}`).
- App Settings → **Expenses**: home organization, mileage rate (default 0.555), Drive folder ID (connect/create later).
- Draft reports: one per user per calendar month. Employees add line items (closed categories including Software), Purpose of trip, and cover period. Mileage amount = miles × rate. Totals by category.
- Private App Drive owner OAuth (separate from the per-user Google Drive/Calendar grant), folder layout, receipt upload/download/proxy.
- Draft → Submit → Reimbursed workflow, with Unsubmit and Undo-reimbursed for Manager:Expenses+.
- PDF export: modern QuestPDF packet plus a legacy Form 87-43 layout (Excel-templated, printed via Excel COM automation when available on the host; otherwise the filled `.xlsx` workbook is returned for the user to print themselves).
- Signatures (employee + manager) captured under Settings → Expenses and stamped onto the legacy PDF.
- Expenses Data page (`ExpenseData.razor`) — org-wide report/line browsing and filtering for Manager:Expenses+, separate from the Tyme Data page.

## Still to build (v1)

- Dashboard reminder for un-submitted reports
- QuickBooks push / category→account mapping UI (still just closed categories + SQL money, exportable but not wired to QBO)
- Import of historical `Expense Working` files
- ACS email notification on reimburse (deferred to the company site)

## Out of v1

- QuickBooks API integration
- Any link to Tyme projects

## Decisions log

| Date | Decision | Why |
|---|---|---|
| 2026-09-01 | New Expenses scope, not Tyme | Finance vs time permissions; expenses are company reimbursements |
| 2026-09-01 | Draft → Submit only | Match Tyme month lock; approval later |
| 2026-09-01 | Modern line items | Recent reports are SaaS/phone (Misc Z), not travel grids |
| 2026-09-01 | HomeOrganizationId | the home company exists in Organizations but was not marked as “us” |
| 2026-09-01 | Privileged Drive owner | Intranet-style shared folder would leak receipts in Google Drive |
| 2026-09-01 | QuickBooks later | Books are QBO; keep SQL + closed categories exportable |
| 2026-09-04 | Employee form is lines only | Real reports leave Department, Mail check to, and Purpose blank; Name/Home Office/the home company are identity, not typed each month |
| 2026-09-17 | Added Purpose of trip field to the editor | Field existed on the entity/DTO/PDF but had no input, so it was always blank |
| 2026-09-17 | Address snapshot refreshes on every draft save, not just create | Editing home address in Settings after creating a report left the printed PDF address stale |
| 2026-09-17 | Submit files the statement PDF under Expenses/Filed | Packet is built on demand today, so user-delete of the receipt folder would destroy the only copy. Filed PDFs sit outside the person folder and survive account delete. Unsubmit drops the file so the next submit files a new one. |
