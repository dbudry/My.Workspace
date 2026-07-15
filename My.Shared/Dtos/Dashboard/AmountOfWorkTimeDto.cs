namespace My.Shared.Dtos.Dashboard
{
    public class AmountOfWorkTimeDto
    {
        public string AmountWorkTimeText { get; set; } = null!;
        public double AmountWorkTime { get; set; }

        public string TopProject { get; set; } = null!;
        public string TopProjectAmounTimeText { get; set; } = null!;
        public double TopProjectAmounTime { get; set; }
    }
}
