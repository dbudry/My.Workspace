using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using My.DAL.Models;
using My.Shared.Constants;

namespace My.DAL.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
    {
        public DbSet<ApplicationUser> ApplicationUsers { get; set; }
        public DbSet<ApplicationRole> ApplicationRoles { get; set; }
        public DbSet<TrackedTask> TrackedTasks { get; set; } = null!;
        public DbSet<StopwatchItem> StopwatchItems { get; set; } = null!;
        public DbSet<Project> Projects { get; set; } = null!;
        public DbSet<Organization> Organizations { get; set; } = null!;
        public DbSet<Department> Departments { get; set; } = null!;
        public DbSet<Contact> Contacts { get; set; } = null!;
        public DbSet<ProjectGroup> ProjectGroups { get; set; } = null!;
        public DbSet<UserSettings> UserSettings { get; set; } = null!;
        public DbSet<AppSetting> AppSettings { get; set; } = null!;
        public DbSet<TimeSubmission> TimeSubmissions { get; set; } = null!;
        public DbSet<TrackedTaskAlias> TrackedTaskAliases { get; set; } = null!;
        public DbSet<TrackedTaskCorrectionAudit> TrackedTaskCorrectionAudits { get; set; } = null!;

        // Intranet / Knowledge Base (mini site builder + Google Drive document library)
        public DbSet<IntranetPage> IntranetPages { get; set; } = null!;
        public DbSet<IntranetDocument> IntranetDocuments { get; set; } = null!;
        public DbSet<IntranetPageDocument> IntranetPageDocuments { get; set; } = null!;
        public DbSet<IntranetNavigationItem> IntranetNavigationItems { get; set; } = null!;
        public DbSet<ExpenseReport> ExpenseReports { get; set; } = null!;
        public DbSet<ExpenseLine> ExpenseLines { get; set; } = null!;
        public DbSet<ExpenseReceipt> ExpenseReceipts { get; set; } = null!;
        public DbSet<AppDriveCredential> AppDriveCredentials { get; set; } = null!;
        public DbSet<Opportunity> Opportunities { get; set; } = null!;
        public DbSet<CrmActivity> CrmActivities { get; set; } = null!;

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Organization (renamed from Customer)
            builder.Entity<Organization>()
                .Property(x => x.OrganizationId)
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Entity<Organization>().HasKey(o => o.OrganizationId);

            // Department
            builder.Entity<Department>()
                .Property(x => x.DepartmentId)
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Entity<Department>().HasKey(d => d.DepartmentId);

            // NoAction (not Cascade) to break a SQL Server "multiple cascade paths" check:
            // Org → Dept CASCADE + Org → Project SetNull + Dept → Project SetNull would form
            // overlapping paths to Project. Org delete now refuses if any Department still
            // exists under it — caller (OrganizationFunction) must clean up Departments first.
            // Gated behind the AllowOrganizationDelete AppSetting anyway, so explicit cleanup
            // before delete is acceptable.
            builder.Entity<Department>()
                .HasOne(d => d.Organization)
                .WithMany(o => o.Departments)
                .HasForeignKey(d => d.OrganizationId)
                .OnDelete(DeleteBehavior.NoAction);

            // Contact
            builder.Entity<Contact>()
                .Property(x => x.ContactId)
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Entity<Contact>().HasKey(c => c.ContactId);

            builder.Entity<Contact>()
                .HasOne(c => c.Organization)
                .WithMany(o => o.Contacts)
                .HasForeignKey(c => c.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Contact>()
                .HasOne(c => c.Department)
                .WithMany(d => d.Contacts)
                .HasForeignKey(c => c.DepartmentId)
                .OnDelete(DeleteBehavior.NoAction);

            // Project
            builder.Entity<Project>()
                .Property(x => x.ProjectId)
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Entity<Project>().HasKey(t => t.ProjectId);

            // TrackedTask
            builder.Entity<TrackedTask>()
                .Property(x => x.TaskId)
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Entity<TrackedTask>()
                .Property(x => x.Details)
                .IsRequired(false)
                .HasMaxLength(500);

            builder.Entity<TrackedTask>()
                .Property(x => x.StartDate)
                .IsRequired();

            builder.Entity<TrackedTask>()
                .Property(x => x.Duration)
                .IsRequired();

            builder.Entity<TrackedTask>().HasKey(t => t.TaskId);

            // Indexes for analytics/dashboard queries
            builder.Entity<TrackedTask>()
                .HasIndex(t => new { t.UserId, t.StartDate });

            builder.Entity<TrackedTask>()
                .HasIndex(t => t.ProjectId);

            builder.Entity<TrackedTask>()
                .HasIndex(t => new { t.StopwatchItemId, t.StartDate });

            // StopwatchItem
            builder.Entity<StopwatchItem>()
                .Property(x => x.StopwatchItemId)
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Entity<StopwatchItem>().HasKey(i => i.StopwatchItemId);

            builder.Entity<StopwatchItem>()
                .Property(x => x.Details)
                .IsRequired()
                .HasMaxLength(500);

            builder.Entity<StopwatchItem>()
                .HasIndex(i => new { i.UserId, i.LastWorkedAt });

            builder.Entity<StopwatchItem>()
                .HasOne(i => i.User)
                .WithMany()
                .HasForeignKey(i => i.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<StopwatchItem>()
                .HasOne(i => i.Project)
                .WithMany()
                .HasForeignKey(i => i.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);

            // Relationships
            builder.Entity<TrackedTask>()
                .HasOne(p => p.Project)
                .WithMany(t => t.TrackedTasks)
                .HasForeignKey(t => t.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<TrackedTask>()
                .HasOne(t => t.User)
                .WithMany(u => u.TrackedTasks)
                .HasForeignKey(t => t.UserId);

            builder.Entity<TrackedTask>()
                .HasOne(t => t.StopwatchItem)
                .WithMany(i => i.Sessions)
                .HasForeignKey(t => t.StopwatchItemId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.Entity<Project>()
                .HasOne(p => p.Organization)
                .WithMany(o => o.Projects)
                .HasForeignKey(p => p.OrganizationId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Project>()
                .HasOne(p => p.Department)
                .WithMany(d => d.Projects)
                .HasForeignKey(p => p.DepartmentId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Project>()
                .HasOne(p => p.ProjectGroup)
                .WithMany(g => g.Projects)
                .HasForeignKey(p => p.ProjectGroupId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Project>()
                .Property(p => p.Name)
                .IsRequired()
                .HasMaxLength(100);

            builder.Entity<Project>()
                .Property(p => p.Slug)
                .HasMaxLength(10);

            builder.Entity<Project>()
                .Property(p => p.DisplayName)
                .HasMaxLength(100);

            builder.Entity<Project>()
                .Property(p => p.CountsAsTime)
                .HasDefaultValue(true);

            // Project slug is the calendar tag — `[slug]` in a Google Calendar event title
            // routes to this project on inbound sync. Workspace-wide unique. SQL Server's
            // filtered-index syntax skips rows where Slug IS NULL so projects without a
            // calendar tag aren't constrained.
            builder.Entity<Project>()
                .HasIndex(p => p.Slug)
                .IsUnique()
                .HasDatabaseName("IX_Projects_Slug")
                .HasFilter("[Slug] IS NOT NULL");

            // ProjectGroup
            builder.Entity<ProjectGroup>()
                .Property(x => x.ProjectGroupId)
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Entity<ProjectGroup>().HasKey(g => g.ProjectGroupId);

            builder.Entity<ProjectGroup>()
                .Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(100);

            // UserSettings
            builder.Entity<UserSettings>()
                .Property(x => x.UserSettingsId)
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Entity<UserSettings>().HasKey(s => s.UserSettingsId);

            builder.Entity<UserSettings>()
                .HasOne(s => s.User)
                .WithOne()
                .HasForeignKey<UserSettings>(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<UserSettings>()
                .Property(s => s.Use24HourTime)
                .HasDefaultValue(false);

            builder.Entity<UserSettings>()
                .Property(s => s.TimeZone)
                .HasMaxLength(100);

            builder.Entity<AppSetting>().HasKey(s => s.Key);

            // TimeSubmission — one row per (User, Year, Month) marks that user
            // submitted that month for completion. Used to lock further edits to
            // tracked tasks falling in that month.
            builder.Entity<TimeSubmission>()
                .Property(x => x.TimeSubmissionId)
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Entity<TimeSubmission>().HasKey(s => s.TimeSubmissionId);

            builder.Entity<TimeSubmission>()
                .HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<TimeSubmission>()
                .HasIndex(s => new { s.UserId, s.Year, s.Month })
                .IsUnique();

            // TrackedTaskAlias — manager/admin override of a user's submitted task.
            // One alias per TaskId; cascades when the original task is deleted.
            builder.Entity<TrackedTaskAlias>()
                .Property(x => x.TrackedTaskAliasId)
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Entity<TrackedTaskAlias>().HasKey(a => a.TrackedTaskAliasId);

            builder.Entity<TrackedTaskAlias>()
                .HasOne(a => a.Task)
                .WithMany()
                .HasForeignKey(a => a.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<TrackedTaskAlias>()
                .HasOne(a => a.Project)
                .WithMany()
                .HasForeignKey(a => a.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<TrackedTaskAlias>()
                .HasIndex(a => a.TaskId)
                .IsUnique();

            // TrackedTaskCorrectionAudit — manager direct correction history (one per TaskId).
            builder.Entity<TrackedTaskCorrectionAudit>()
                .Property(x => x.TrackedTaskCorrectionAuditId)
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Entity<TrackedTaskCorrectionAudit>().HasKey(a => a.TrackedTaskCorrectionAuditId);

            builder.Entity<TrackedTaskCorrectionAudit>()
                .HasOne(a => a.Task)
                .WithMany()
                .HasForeignKey(a => a.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<TrackedTaskCorrectionAudit>()
                .HasIndex(a => a.TaskId)
                .IsUnique();

            // === Intranet / Knowledge Base (mini site builder + Drive document library) ===

            // IntranetPage - hierarchical content pages
            builder.Entity<IntranetPage>()
                .Property(x => x.PageId)
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Entity<IntranetPage>().HasKey(p => p.PageId);

            builder.Entity<IntranetPage>()
                .HasOne(p => p.ParentPage)
                .WithMany(p => p.ChildPages)
                .HasForeignKey(p => p.ParentPageId)
                .OnDelete(DeleteBehavior.Restrict); // prevent cycles on delete

            builder.Entity<IntranetPage>()
                .Property(p => p.Slug)
                .HasMaxLength(200);

            builder.Entity<IntranetPage>()
                .HasIndex(p => p.Slug)
                .IsUnique()
                .HasDatabaseName("IX_IntranetPages_Slug")
                .HasFilter("[Slug] IS NOT NULL");

            builder.Entity<IntranetPage>()
                .Property(p => p.Title)
                .IsRequired()
                .HasMaxLength(200);

            builder.Entity<IntranetPage>()
                .Property(p => p.Visibility)
                .HasMaxLength(50)
                .HasDefaultValue("Default");

            // IntranetDocument - curated Drive file registry + annotations
            builder.Entity<IntranetDocument>()
                .Property(x => x.DocumentId)
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Entity<IntranetDocument>().HasKey(d => d.DocumentId);

            builder.Entity<IntranetDocument>()
                .HasIndex(d => d.DriveFileId)
                .IsUnique()
                .HasDatabaseName("IX_IntranetDocuments_DriveFileId");

            builder.Entity<IntranetDocument>()
                .Property(d => d.Name)
                .IsRequired()
                .HasMaxLength(500);

            builder.Entity<IntranetDocument>()
                .Property(d => d.Category)
                .HasMaxLength(100);

            // Junction: Page <-> Document (with per-page order + caption)
            builder.Entity<IntranetPageDocument>()
                .HasKey(pd => new { pd.PageId, pd.DocumentId });

            builder.Entity<IntranetPageDocument>()
                .HasOne(pd => pd.Page)
                .WithMany(p => p.PageDocuments)
                .HasForeignKey(pd => pd.PageId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<IntranetPageDocument>()
                .HasOne(pd => pd.Document)
                .WithMany(d => d.PageDocuments)
                .HasForeignKey(pd => pd.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            // IntranetNavigationItem - curated top-level (and sub) navigation managed by intranet admins
            builder.Entity<IntranetNavigationItem>()
                .Property(x => x.Id)
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Entity<IntranetNavigationItem>().HasKey(n => n.Id);

            builder.Entity<IntranetNavigationItem>()
                .HasOne(n => n.Page)
                .WithMany()
                .HasForeignKey(n => n.PageId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<IntranetNavigationItem>()
                .HasOne(n => n.Parent)
                .WithMany(n => n.Children)
                .HasForeignKey(n => n.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<IntranetNavigationItem>()
                .Property(n => n.Title)
                .IsRequired()
                .HasMaxLength(100);

            builder.Entity<IntranetNavigationItem>()
                .Property(n => n.ExternalUrl)
                .HasMaxLength(500);

            builder.Entity<IntranetNavigationItem>()
                .Property(n => n.Icon)
                .HasMaxLength(50);

            builder.Entity<ExpenseReport>()
                .HasKey(r => r.ExpenseReportId);

            builder.Entity<ExpenseReport>()
                .HasIndex(r => new { r.UserId, r.Year, r.Month })
                .IsUnique()
                .HasDatabaseName("IX_ExpenseReports_UserId_Year_Month");

            builder.Entity<ExpenseReport>()
                .HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ExpenseReport>()
                .HasOne(r => r.Department)
                .WithMany()
                .HasForeignKey(r => r.DepartmentId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<ExpenseReport>()
                .Property(r => r.MileageRateSnapshot)
                .HasPrecision(18, 4);

            builder.Entity<ExpenseLine>()
                .HasKey(l => l.ExpenseLineId);

            builder.Entity<ExpenseLine>()
                .HasOne(l => l.ExpenseReport)
                .WithMany(r => r.Lines)
                .HasForeignKey(l => l.ExpenseReportId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ExpenseLine>()
                .Property(l => l.Amount)
                .HasPrecision(18, 2);

            builder.Entity<ExpenseLine>()
                .Property(l => l.Miles)
                .HasPrecision(18, 3);

            builder.Entity<ExpenseReceipt>()
                .HasKey(r => r.ExpenseReceiptId);

            builder.Entity<ExpenseReceipt>()
                .HasOne(r => r.ExpenseLine)
                .WithMany(l => l.Receipts)
                .HasForeignKey(r => r.ExpenseLineId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<AppDriveCredential>()
                .HasKey(c => c.AppDriveCredentialId);

            builder.Entity<Opportunity>()
                .Property(o => o.OpportunityId)
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Entity<Opportunity>().HasKey(o => o.OpportunityId);

            builder.Entity<Opportunity>()
                .Property(o => o.Name)
                .IsRequired()
                .HasMaxLength(200);

            builder.Entity<Opportunity>()
                .Property(o => o.Stage)
                .IsRequired()
                .HasMaxLength(20);

            builder.Entity<Opportunity>()
                .Property(o => o.Amount)
                .HasPrecision(18, 2);

            builder.Entity<Opportunity>()
                .Property(o => o.ExpectedCloseDate)
                .HasColumnType("date");

            builder.Entity<Opportunity>()
                .Property(o => o.OwnerUserId)
                .HasMaxLength(450);

            builder.Entity<Opportunity>()
                .Property(o => o.ContactId)
                .HasMaxLength(450);

            builder.Entity<Opportunity>()
                .Property(o => o.Note)
                .HasMaxLength(500);

            // SetNull so an organization delete still succeeds. ContactId is intentionally
            // not a foreign key — see Opportunity.ContactId.
            builder.Entity<Opportunity>()
                .HasOne(o => o.Organization)
                .WithMany()
                .HasForeignKey(o => o.OrganizationId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Opportunity>()
                .HasIndex(o => o.OrganizationId);

            builder.Entity<Opportunity>()
                .HasIndex(o => o.Stage);

            builder.Entity<CrmActivity>()
                .Property(a => a.CrmActivityId)
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Entity<CrmActivity>().HasKey(a => a.CrmActivityId);

            builder.Entity<CrmActivity>()
                .Property(a => a.ActivityType)
                .IsRequired()
                .HasMaxLength(20);

            builder.Entity<CrmActivity>()
                .Property(a => a.Subject)
                .IsRequired()
                .HasMaxLength(200);

            builder.Entity<CrmActivity>()
                .Property(a => a.Body)
                .HasMaxLength(500);

            builder.Entity<CrmActivity>()
                .Property(a => a.OwnerUserId)
                .HasMaxLength(450);

            builder.Entity<CrmActivity>()
                .HasOne(a => a.Opportunity)
                .WithMany(o => o.Activities)
                .HasForeignKey(a => a.OpportunityId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<CrmActivity>()
                .HasIndex(a => a.OpportunityId);

            FillDataToDB(builder);
        }

        private static void FillDataToDB(ModelBuilder builder)
        {
            builder.Entity<AppSetting>().HasData(
                new AppSetting
                {
                    Key = "AllowUserDelete",
                    Value = "false",
                    Description = "Allow administrators to delete users"
                },
                new AppSetting
                {
                    Key = "DataRetentionDays",
                    Value = "2555",
                    Description = "Days of data retention before a user can be deleted"
                },
                new AppSetting
                {
                    Key = "AllowOrganizationDelete",
                    Value = "false",
                    Description = "Allow managers to delete organizations"
                },
                new AppSetting
                {
                    Key = "AllowProjectDelete",
                    Value = "false",
                    Description = "Allow managers to delete projects"
                },
                new AppSetting
                {
                    Key = "TymeSubmissionMonthInterval",
                    Value = "1",
                    Description = "How often (in months) employees are expected to submit their time."
                },
                new AppSetting
                {
                    Key = "TymeAllowManagerTimeCorrection",
                    Value = "true",
                    Description = "When enabled, Tyme managers and admins can correct submitted employee time entries."
                },
                new AppSetting
                {
                    Key = "TymeAllowManagerSubmitOnBehalf",
                    Value = "false",
                    Description = "When enabled, Tyme managers and admins can submit an employee's time month on their behalf."
                },
                new AppSetting
                {
                    Key = "TymeAllowUserTeamReports",
                    Value = "false",
                    Description = "When enabled, Tyme users can view other employees' time on Reports (read-only)."
                },
                new AppSetting
                {
                    Key = "TymeManagerCorrectionMode",
                    Value = "Alias",
                    Description = "When manager correction is enabled: Alias (overlay, original preserved) or Direct (in-place edit with audit)."
                },

                new AppSetting
                {
                    Key = "TymeCalendarBackfillDefaultDays",
                    Value = "30",
                    Description = "Default lookback window (in days) when pushing existing tracked tasks onto a newly-connected user's primary Google calendar."
                },
                new AppSetting
                {
                    Key = "TymeCalendarBackfillPromptUser",
                    Value = "true",
                    Description = "When true, users are prompted to choose a date range (or skip) on connect. When false, the default lookback above is applied silently."
                },
                new AppSetting
                {
                    Key = "WorkdayHours",
                    Value = "8",
                    Description = "How many hours count as a full workday. Drives the derived Duration for all-day entries (Vacation/OOO)."
                },
                new AppSetting
                {
                    Key = "TeamAvailabilityCalendarId",
                    Value = "",
                    Description = "Google calendar id of the workspace-shared 'Team Availability' calendar. Empty disables the team-availability dual-publish feature."
                },
                new AppSetting
                {
                    Key = "ContactTypes",
                    Value = "[\"Primary\",\"Billing\",\"Maintenance\"]",
                    Description = "JSON array of allowed contact types for organization and department contacts."
                },
                new AppSetting
                {
                    Key = "IntranetNavigationMaxDepth",
                    Value = "10",
                    Description = "Maximum nesting depth for curated intranet sidebar navigation. Top-level menu entries count as depth 1."
                },
                new AppSetting
                {
                    Key = "HomeOrganizationId",
                    Value = "",
                    Description = "OrganizationId of the home company."
                },
                new AppSetting
                {
                    Key = "ExpensesMileageRatePerMile",
                    Value = "0.555",
                    Description = "Personal-car mileage reimbursement rate in USD per mile."
                },
                new AppSetting
                {
                    Key = "ExpensesDriveParentFolderId",
                    Value = "",
                    Description = "Google Drive folder ID for the private Expenses root. Not shared company-wide."
                });

            builder.Entity<ApplicationRole>().HasData(
                // Global roles
                new ApplicationRole
                {
                    Id = "d1e2f3a4-b5c6-7890-d1e2-f3a4b5c67890",
                    Name = Constants.Roles.User,
                    NormalizedName = Constants.Roles.User.ToUpper(),
                    Description = "Basic role with lowest rights.",
                    ConcurrencyStamp = "d1e2f3a4-b5c6-7890-d1e2-f3a4b5c67890"
                },
                new ApplicationRole
                {
                    Id = "6dec8b09-9fa0-474b-ae23-b4267c2c516a",
                    Name = Constants.Roles.Admin,
                    NormalizedName = Constants.Roles.Admin.ToUpper(),
                    Description = "Admin role with highest rights.",
                    ConcurrencyStamp = "6dec8b09-9fa0-474b-ae23-b4267c2c516a"
                },
                new ApplicationRole
                {
                    Id = "f72e9c7c-2517-427b-b08b-56966c51f5d4",
                    Name = Constants.Roles.Manager,
                    NormalizedName = Constants.Roles.Manager.ToUpper(),
                    Description = "Manager role for for users and reports",
                    ConcurrencyStamp = "f72e9c7c-2517-427b-b08b-56966c51f5d4"
                },
                // Scoped roles for Tyme
                new ApplicationRole
                {
                    Id = "a1b2c3d4-e5f6-7890-a1b2-c3d4e5f67890",
                    Name = Constants.Roles.Scoped(Constants.Roles.User, Constants.Scopes.Tyme),
                    NormalizedName = Constants.Roles.Scoped(Constants.Roles.User, Constants.Scopes.Tyme).ToUpper(),
                    Description = "Tyme-scoped basic user role.",
                    ConcurrencyStamp = "a1b2c3d4-e5f6-7890-a1b2-c3d4e5f67890"
                },
                new ApplicationRole
                {
                    Id = "b2c3d4e5-f6a7-8901-b2c3-d4e5f6a78901",
                    Name = Constants.Roles.Scoped(Constants.Roles.Manager, Constants.Scopes.Tyme),
                    NormalizedName = Constants.Roles.Scoped(Constants.Roles.Manager, Constants.Scopes.Tyme).ToUpper(),
                    Description = "Tyme-scoped manager role.",
                    ConcurrencyStamp = "b2c3d4e5-f6a7-8901-b2c3-d4e5f6a78901"
                },
                new ApplicationRole
                {
                    Id = "20a1b2c3-d4e5-4678-9abc-def012345701",
                    Name = Constants.Roles.Scoped(Constants.Roles.UserAccess, Constants.Scopes.Tyme),
                    NormalizedName = Constants.Roles.Scoped(Constants.Roles.UserAccess, Constants.Scopes.Tyme).ToUpper(),
                    Description = "Tyme User Access — assign Tyme roles on Users. Does not operate Tyme.",
                    ConcurrencyStamp = "20a1b2c3-d4e5-4678-9abc-def012345701"
                },
                // Scoped roles for Intranet (knowledge base / internal pages + Drive docs)
                new ApplicationRole
                {
                    Id = "d4e5f6a7-b8c9-0123-d4e5-f6a7b8c90123",
                    Name = Constants.Roles.Scoped(Constants.Roles.User, Constants.Scopes.Intranet),
                    NormalizedName = Constants.Roles.Scoped(Constants.Roles.User, Constants.Scopes.Intranet).ToUpper(),
                    Description = "Intranet-scoped basic user role (view published pages and curated navigation).",
                    ConcurrencyStamp = "d4e5f6a7-b8c9-0123-d4e5-f6a7b8c90123"
                },
                new ApplicationRole
                {
                    Id = "e5f6a7b8-c9d0-1234-e5f6-a7b8c9d01234",
                    Name = Constants.Roles.Scoped(Constants.Roles.Editor, Constants.Scopes.Intranet),
                    NormalizedName = Constants.Roles.Scoped(Constants.Roles.Editor, Constants.Scopes.Intranet).ToUpper(),
                    Description = "Intranet-scoped editor role (create/edit pages and manage documents).",
                    ConcurrencyStamp = "e5f6a7b8-c9d0-1234-e5f6-a7b8c9d01234"
                },
                new ApplicationRole
                {
                    Id = "20a1b2c3-d4e5-4678-9abc-def012345705",
                    Name = Constants.Roles.Scoped(Constants.Roles.Navigation, Constants.Scopes.Intranet),
                    NormalizedName = Constants.Roles.Scoped(Constants.Roles.Navigation, Constants.Scopes.Intranet).ToUpper(),
                    Description = "Intranet Navigation — curated sidebar tree.",
                    ConcurrencyStamp = "20a1b2c3-d4e5-4678-9abc-def012345705"
                },
                new ApplicationRole
                {
                    Id = "20a1b2c3-d4e5-4678-9abc-def012345702",
                    Name = Constants.Roles.Scoped(Constants.Roles.UserAccess, Constants.Scopes.Intranet),
                    NormalizedName = Constants.Roles.Scoped(Constants.Roles.UserAccess, Constants.Scopes.Intranet).ToUpper(),
                    Description = "Intranet User Access — assign Intranet roles on Users. Does not operate Intranet.",
                    ConcurrencyStamp = "20a1b2c3-d4e5-4678-9abc-def012345702"
                },
                new ApplicationRole
                {
                    Id = "10a1b2c3-d4e5-4678-9abc-def012345601",
                    Name = Constants.Roles.Scoped(Constants.Roles.User, Constants.Scopes.Expenses),
                    NormalizedName = Constants.Roles.Scoped(Constants.Roles.User, Constants.Scopes.Expenses).ToUpper(),
                    Description = "Expenses-scoped user role (own reports and receipts).",
                    ConcurrencyStamp = "10a1b2c3-d4e5-4678-9abc-def012345601"
                },
                new ApplicationRole
                {
                    Id = "10a1b2c3-d4e5-4678-9abc-def012345602",
                    Name = Constants.Roles.Scoped(Constants.Roles.Manager, Constants.Scopes.Expenses),
                    NormalizedName = Constants.Roles.Scoped(Constants.Roles.Manager, Constants.Scopes.Expenses).ToUpper(),
                    Description = "Expenses-scoped manager role (team reports and unsubmit).",
                    ConcurrencyStamp = "10a1b2c3-d4e5-4678-9abc-def012345602"
                },
                new ApplicationRole
                {
                    Id = "20a1b2c3-d4e5-4678-9abc-def012345704",
                    Name = Constants.Roles.Scoped(Constants.Roles.UserAccess, Constants.Scopes.Expenses),
                    NormalizedName = Constants.Roles.Scoped(Constants.Roles.UserAccess, Constants.Scopes.Expenses).ToUpper(),
                    Description = "Expenses User Access — assign Expenses roles on Users. Does not operate Expenses.",
                    ConcurrencyStamp = "20a1b2c3-d4e5-4678-9abc-def012345704"
                },
                new ApplicationRole
                {
                    Id = "20a1b2c3-d4e5-4678-9abc-def012345707",
                    Name = Constants.Roles.Scoped(Constants.Roles.User, Constants.Scopes.Organizations),
                    NormalizedName = Constants.Roles.Scoped(Constants.Roles.User, Constants.Scopes.Organizations).ToUpper(),
                    Description = "Organizations-scoped user role (view).",
                    ConcurrencyStamp = "20a1b2c3-d4e5-4678-9abc-def012345707"
                },
                new ApplicationRole
                {
                    Id = "20a1b2c3-d4e5-4678-9abc-def012345708",
                    Name = Constants.Roles.Scoped(Constants.Roles.Editor, Constants.Scopes.Organizations),
                    NormalizedName = Constants.Roles.Scoped(Constants.Roles.Editor, Constants.Scopes.Organizations).ToUpper(),
                    Description = "Organizations-scoped editor role (create/edit organizations and departments).",
                    ConcurrencyStamp = "20a1b2c3-d4e5-4678-9abc-def012345708"
                },
                new ApplicationRole
                {
                    Id = "20a1b2c3-d4e5-4678-9abc-def012345706",
                    Name = Constants.Roles.Scoped(Constants.Roles.Manager, Constants.Scopes.Organizations),
                    NormalizedName = Constants.Roles.Scoped(Constants.Roles.Manager, Constants.Scopes.Organizations).ToUpper(),
                    Description = "Organizations-scoped manager role (archive, delete, set active/inactive; includes edit).",
                    ConcurrencyStamp = "20a1b2c3-d4e5-4678-9abc-def012345706"
                },
                new ApplicationRole
                {
                    Id = "20a1b2c3-d4e5-4678-9abc-def012345703",
                    Name = Constants.Roles.Scoped(Constants.Roles.UserAccess, Constants.Scopes.Organizations),
                    NormalizedName = Constants.Roles.Scoped(Constants.Roles.UserAccess, Constants.Scopes.Organizations).ToUpper(),
                    Description = "Organizations User Access — assign Organizations roles on Users.",
                    ConcurrencyStamp = "20a1b2c3-d4e5-4678-9abc-def012345703"
                },
                new ApplicationRole
                {
                    Id = "30a1b2c3-d4e5-4678-9abc-def012345801",
                    Name = Constants.Roles.Scoped(Constants.Roles.User, Constants.Scopes.Crm),
                    NormalizedName = Constants.Roles.Scoped(Constants.Roles.User, Constants.Scopes.Crm).ToUpper(),
                    Description = "CRM-scoped user role (view the pipeline).",
                    ConcurrencyStamp = "30a1b2c3-d4e5-4678-9abc-def012345801"
                },
                new ApplicationRole
                {
                    Id = "30a1b2c3-d4e5-4678-9abc-def012345802",
                    Name = Constants.Roles.Scoped(Constants.Roles.Editor, Constants.Scopes.Crm),
                    NormalizedName = Constants.Roles.Scoped(Constants.Roles.Editor, Constants.Scopes.Crm).ToUpper(),
                    Description = "CRM-scoped editor role (create and edit opportunities and activities).",
                    ConcurrencyStamp = "30a1b2c3-d4e5-4678-9abc-def012345802"
                },
                new ApplicationRole
                {
                    Id = "30a1b2c3-d4e5-4678-9abc-def012345803",
                    Name = Constants.Roles.Scoped(Constants.Roles.Manager, Constants.Scopes.Crm),
                    NormalizedName = Constants.Roles.Scoped(Constants.Roles.Manager, Constants.Scopes.Crm).ToUpper(),
                    Description = "CRM-scoped manager role (archive and delete).",
                    ConcurrencyStamp = "30a1b2c3-d4e5-4678-9abc-def012345803"
                },
                new ApplicationRole
                {
                    Id = "30a1b2c3-d4e5-4678-9abc-def012345804",
                    Name = Constants.Roles.Scoped(Constants.Roles.UserAccess, Constants.Scopes.Crm),
                    NormalizedName = Constants.Roles.Scoped(Constants.Roles.UserAccess, Constants.Scopes.Crm).ToUpper(),
                    Description = "CRM User Access — assign CRM roles on Users. Does not operate CRM.",
                    ConcurrencyStamp = "30a1b2c3-d4e5-4678-9abc-def012345804"
                });

            // Note: If you add/remove roles, AppSettings, or other HasData here,
            // run: dotnet ef migrations add YourMigrationName --project My.DAL --startup-project My.AzureFunction
        }

    }
}
