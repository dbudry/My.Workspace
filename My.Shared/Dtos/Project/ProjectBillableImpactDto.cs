namespace My.Shared.Dtos.Project
{
    /// <summary>
    /// How many tracked tasks on a project are currently marked billable — used before
    /// a manager turns off <see cref="ProjectDto.IsBillable"/>.
    /// </summary>
    public class ProjectBillableImpactDto
    {
        public int BillableTaskCount { get; set; }
        public bool HasBillableTime => BillableTaskCount > 0;
    }
}