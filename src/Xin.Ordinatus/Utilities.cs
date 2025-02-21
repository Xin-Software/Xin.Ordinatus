namespace Xin.Ordinatus.Utilities;

public record ValidationResult
{
    private List<ValidationFailure> errors;

    public bool IsValid => this.errors.Count == 0;

    public IReadOnlyList<ValidationFailure> Errors => this.errors;

    public ValidationResult()
    {
        this.errors = new List<ValidationFailure>();
    }

    public ValidationResult(IEnumerable<ValidationFailure> errors)
    {
        this.errors = errors.Where(x => x != null).ToList();
    }
}

public record ValidationFailure
{
    /// <summary>
    /// Gets the name of the property that failed validation.
    /// </summary>
    required public string PropertyName { get; init; }

    /// <summary>
    /// Gets the error message for the validation failure.
    /// </summary>
    required public string ErrorMessage { get; init; }
}
