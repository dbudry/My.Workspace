using My.Shared.Rules;

namespace My.Client.Models.Dashboard
{
    /// <summary>
    /// Re-pivots a flat list of per-project time data into whichever axis the user has
    /// selected (Organization / Project / Project Group), and resolves the matching
    /// per-segment color palette. Shared by the Dashboard's "Project mix" doughnut and
    /// the Reports page's "Time by Project" doughnut + Daily Hours breakdown so all three
    /// group and color data identically.
    /// </summary>
    public static class ChartPivotRules
    {
        /// <summary>
        /// Groups <paramref name="data"/> by the selected axis, rolling entries with no
        /// parent (no org / no group) into a single "Unspecified" bucket. Project axis is
        /// the identity pivot ΓÇö every item keeps its own project as its own segment.
        /// Results are sorted by time, descending.
        /// </summary>
        public static List<ProjectDataItem> Pivot(IEnumerable<ProjectDataItem> data, ChartAxis axis)
        {
            if (axis == ChartAxis.Project)
            {
                return data
                    .GroupBy(p => (p.ProjectId, p.ProjectName))
                    .Select(g =>
                    {
                        var sample = g.First();
                        return new ProjectDataItem(
                            g.Key.ProjectId,
                            g.Key.ProjectName,
                            TimeSpan.FromSeconds(g.Sum(p => p.Time.TotalSeconds)),
                            "")
                        {
                            OrganizationColor = sample.OrganizationColor,
                            ProjectGroupColor = sample.ProjectGroupColor,
                        };
                    })
                    .OrderByDescending(p => p.Time)
                    .ToList();
            }

            IEnumerable<IGrouping<(string? Id, string? Name), ProjectDataItem>> grouped = axis switch
            {
                ChartAxis.Organization => data.GroupBy(p => (p.OrganizationId, p.OrganizationName)),
                ChartAxis.ProjectGroup => data.GroupBy(p => (p.ProjectGroupId, p.ProjectGroupName)),
                _ => data.GroupBy(p => (p.OrganizationId, p.OrganizationName))
            };

            return grouped
                .Select(g =>
                {
                    var sample = g.First();
                    return new ProjectDataItem(
                        g.Key.Id ?? "Unspecified",
                        string.IsNullOrEmpty(g.Key.Name) ? "Unspecified" : g.Key.Name!,
                        TimeSpan.FromSeconds(g.Sum(p => p.Time.TotalSeconds)),
                        "")
                    {
                        OrganizationColor = sample.OrganizationColor,
                        ProjectGroupColor = sample.ProjectGroupColor,
                    };
                })
                .OrderByDescending(p => p.Time)
                .ToList();
        }

        /// <summary>
        /// Per-segment palette aligned with the output of <see cref="Pivot"/> (same order).
        /// Returns null when the user opted out of project colors entirely ΓÇö callers should
        /// fall back to MudBlazor's default palette in that case.
        /// </summary>
        public static string[]? Palette(List<ProjectDataItem> pivoted, ChartAxis axis, ProjectColorSource source)
        {
            if (source == ProjectColorSource.None)
                return null;

            // Project axis has no color of its own the way Org/Group do. Inheriting via
            // the group-then-org preference meant two different projects under the same
            // org (or group) rendered as the exact same chart color ΓÇö indistinguishable
            // segments. Every project gets its own generated color instead.
            if (axis == ChartAxis.Project)
                return GenerateDistinctPalette(pivoted.Select(p => p.ProjectId).ToList());

            var resolved = pivoted
                .Select(item => axis == ChartAxis.ProjectGroup ? item.ProjectGroupColor : item.OrganizationColor)
                .ToArray();

            // Items with no explicit color would otherwise all collapse to the same
            // fallback gray ΓÇö indistinguishable from each other on the chart. Give each
            // of those a distinct generated color instead; items with a real color keep it.
            var keys = pivoted
                .Select(p => axis == ChartAxis.ProjectGroup ? p.ProjectGroupId : p.OrganizationId)
                .ToList();
            var missingIndices = resolved
                .Select((c, i) => (Color: c, Index: i))
                .Where(x => string.IsNullOrWhiteSpace(x.Color))
                .Select(x => x.Index)
                .ToList();
            var generatedForMissing = GenerateDistinctPalette(missingIndices.Select(i => keys[i]).ToList());

            var result = new string[resolved.Length];
            for (int i = 0; i < resolved.Length; i++)
                result[i] = resolved[i]!;
            for (int j = 0; j < missingIndices.Count; j++)
                result[missingIndices[j]] = generatedForMissing[j];

            return result;
        }

        private const double GoldenAngleDegrees = 137.50776;

        /// <summary>
        /// Assigns each key its own visually distinct color via golden-angle hue stepping
        /// (the same technique used to space points evenly around a circle ΓÇö no two
        /// adjacent hues land close together even as the count grows). Keys are ranked by
        /// their own value, not input order, so the same set of segments gets the same
        /// colors across re-renders regardless of how the chart currently has them sorted
        /// (e.g. by time, descending). Saturation/lightness are fixed at levels that read
        /// clearly on both the light and dark theme backgrounds.
        /// </summary>
        private static string[] GenerateDistinctPalette(List<string?> keys)
        {
            var rank = keys
                .Select((key, index) => (Key: key ?? "", Index: index))
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToList();

            var colors = new string[keys.Count];
            for (int i = 0; i < rank.Count; i++)
            {
                var hue = (i * GoldenAngleDegrees) % 360;
                colors[rank[i].Index] = HslToHex(hue, 0.55, 0.50);
            }
            return colors;
        }

        private static string HslToHex(double hue, double saturation, double lightness)
        {
            double c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
            double x = c * (1 - Math.Abs((hue / 60.0 % 2) - 1));
            double m = lightness - c / 2;

            double r1, g1, b1;
            if (hue < 60) { r1 = c; g1 = x; b1 = 0; }
            else if (hue < 120) { r1 = x; g1 = c; b1 = 0; }
            else if (hue < 180) { r1 = 0; g1 = c; b1 = x; }
            else if (hue < 240) { r1 = 0; g1 = x; b1 = c; }
            else if (hue < 300) { r1 = x; g1 = 0; b1 = c; }
            else { r1 = c; g1 = 0; b1 = x; }

            int r = (int)Math.Round((r1 + m) * 255);
            int g = (int)Math.Round((g1 + m) * 255);
            int b = (int)Math.Round((b1 + m) * 255);
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        public static string AxisLabel(ChartAxis axis) => axis switch
        {
            ChartAxis.Organization => "organization",
            ChartAxis.Project => "project",
            ChartAxis.ProjectGroup => "project group",
            _ => "organization"
        };

        /// <summary>
        /// Resolves the (Id, Name) a single task/project should bucket under for the
        /// selected axis ΓÇö the per-item counterpart to <see cref="Pivot"/>, used when
        /// building a stacked series (e.g. Daily Hours) where each raw item needs to be
        /// assigned to a category bucket rather than the whole set re-grouped at once.
        /// Missing parents roll into "Unspecified" (Org/Group) same as Pivot; a missing
        /// project rolls into "None" to match the existing "Time by Project" convention.
        /// </summary>
        public static (string Key, string Name) CategoryKey(
            string? projectId, string? projectName,
            string? organizationId, string? organizationName,
            string? projectGroupId, string? projectGroupName,
            ChartAxis axis)
        {
            return axis switch
            {
                ChartAxis.Organization => (
                    organizationId ?? "Unspecified",
                    string.IsNullOrEmpty(organizationName) ? "Unspecified" : organizationName!),
                ChartAxis.ProjectGroup => (
                    projectGroupId ?? "Unspecified",
                    string.IsNullOrEmpty(projectGroupName) ? "Unspecified" : projectGroupName!),
                ChartAxis.Project => (
                    projectId ?? "None",
                    string.IsNullOrEmpty(projectName) ? "None" : projectName!),
                _ => (
                    organizationId ?? "Unspecified",
                    string.IsNullOrEmpty(organizationName) ? "Unspecified" : organizationName!)
            };
        }
    }
}
