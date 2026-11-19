using System.ComponentModel.DataAnnotations;

namespace medxbackend.Models;

/// <summary>Creates a routing rule for one form type and supported target system.</summary>
public sealed record CreateSyncRuleRequest
{
    [Required, StringLength(2), RegularExpression("^[A-Z0-9]{1,2}$")]
    public string FormType { get; init; } = string.Empty;

    [Required, RegularExpression("^(LIS|CRM)$")]
    public string TargetSystem { get; init; } = string.Empty;

    public bool IsEnabled { get; init; } = true;
}

/// <summary>Changes the enabled state of an existing rule without changing its primary key.</summary>
public sealed record UpdateSyncRuleRequest
{
    [Required]
    public bool? IsEnabled { get; init; }
}

/// <summary>Represents a form-type synchronization routing rule.</summary>
public sealed record SyncRuleResponse(long Id, string FormType, string TargetSystem, bool IsEnabled);

/// <summary>Confirms deletion of a routing rule selected by form type and target system.</summary>
public sealed record DeleteSyncRuleResponse(bool Success, string Message, string FormType, string TargetSystem);
