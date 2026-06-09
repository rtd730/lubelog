namespace CarCareTracker.Models
{
    public enum FilterOperator
    {
        Eq,         // equals
        Ne,         // not equals
        Gt,         // greater than
        Gte,        // greater than or equal
        Lt,         // less than
        Lte,        // less than or equal
        Contains    // string contains
    }

    public enum FilterLogic
    {
        And,    // all rules must pass
        Or      // any rule can pass
    }

    public class FilterRule
    {
        public string Field { get; set; } = string.Empty;
        public FilterOperator Operator { get; set; }
        public string Value { get; set; } = string.Empty;
    }

    public class FilterDefinition
    {
        public int Id { get; set; }
        public int VehicleId { get; set; }
        public string Name { get; set; } = string.Empty;
        public FilterLogic Logic { get; set; } = FilterLogic.And;
        public List<FilterRule> Rules { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
