namespace My.Shared.Rules;

/// <summary>
/// Rules that gate operations on a TrackedTask based on whether its month
/// has been submitted. Pure functions — no DB access. The caller is
/// responsible for looking up the submitted-month state and passing it in.
///
/// Why a helper: the rules used to live as scattered `if` checks inside
/// each function method, with the error strings inlined. Centralizing them
/// here means (a) every rule is visible in one place, (b) the error
/// messages can be tested, and (c) a new code path that needs to gate on
/// submission can call the same rule instead of copy-pasting a string.
/// </summary>
public static class SubmissionRules
{
    /// <summary>
    /// Operations whose permissibility depends on submission state.
    /// </summary>
    public enum Operation
    {
        /// <summary>Create a new tracked task in the target month.</summary>
        Create,
        /// <summary>Edit a tracked task in its current month.</summary>
        Edit,
        /// <summary>Change a tracked task's date so it moves into a different month.</summary>
        Move,
        /// <summary>Delete a tracked task.</summary>
        Delete,
        /// <summary>Duplicate a tracked task into a target month.</summary>
        Duplicate,
        /// <summary>Manager creates or updates an alias on a tracked task.</summary>
        Alias,
        /// <summary>Manager directly edits a tracked task in place after submission.</summary>
        ManagerDirectEdit,
    }

    /// <summary>
    /// Returns whether the operation is allowed given the submission state.
    /// For most operations, a submitted month blocks the action. For
    /// <see cref="Operation.Alias"/>, the inverse holds: aliases are the
    /// manager's post-submission correction tool and *require* the month
    /// to already be submitted.
    /// </summary>
    public static Decision Evaluate(Operation op, bool isMonthSubmitted)
    {
        return op switch
        {
            Operation.Alias or Operation.ManagerDirectEdit =>
                isMonthSubmitted
                    ? Decision.Allowed()
                    : Decision.Blocked("This task's month must be submitted before a manager can alter it."),

            Operation.Create =>
                isMonthSubmitted
                    ? Decision.Blocked("This month has already been submitted — unsubmit it first to add new time entries.")
                    : Decision.Allowed(),

            Operation.Edit =>
                isMonthSubmitted
                    ? Decision.Blocked("This task is in a submitted month and cannot be edited.")
                    : Decision.Allowed(),

            Operation.Move =>
                isMonthSubmitted
                    ? Decision.Blocked("Cannot move this task into a submitted month.")
                    : Decision.Allowed(),

            Operation.Delete =>
                isMonthSubmitted
                    ? Decision.Blocked("This task is in a submitted month and cannot be deleted.")
                    : Decision.Allowed(),

            Operation.Duplicate =>
                isMonthSubmitted
                    ? Decision.Blocked("Cannot duplicate into a submitted month.")
                    : Decision.Allowed(),

            _ => Decision.Allowed()
        };
    }

    /// <summary>
    /// Outcome of a rule evaluation: either allowed (no reason), or blocked
    /// with a user-facing reason string suitable for a 400-class API response.
    /// </summary>
    public sealed record Decision(bool IsAllowed, string? Reason)
    {
        public static Decision Allowed() => new(true, null);
        public static Decision Blocked(string reason) => new(false, reason);
    }
}
