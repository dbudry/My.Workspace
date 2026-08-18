namespace My.Shared.Rules
{
    public static class TrackedTaskBillableRules
    {
        /// <summary>
        /// Availability/PTO projects are never billable regardless of the flag.
        /// </summary>
        public static bool FromProject(bool projectIsBillable, bool projectIsSharedAvailability)
            => !projectIsSharedAvailability && projectIsBillable;
    }
}