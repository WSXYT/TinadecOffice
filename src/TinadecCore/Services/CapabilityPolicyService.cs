using Tinadec.Contracts.Models;
using Tinadec.Contracts.Security;
using TinadecCore.Abstractions;

namespace TinadecCore.Services;

public sealed class CapabilityPolicyService : ICapabilityPolicy
{
    public ApprovalRequirement Evaluate(string permissionMode, ToolDescriptorDto tool)
    {
        var mode = NormalizePermissionMode(permissionMode);
        return PermissionPolicy.Evaluate(mode, MapRisk(tool.Risk));
    }

    public bool IsReadOnly(ToolDescriptorDto tool)
    {
        return MapRisk(tool.Risk) == ToolRisk.ReadOnly && !tool.RequiresApproval;
    }

    private static PermissionMode NormalizePermissionMode(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "observe" => PermissionMode.Observe,
            "trusted" => PermissionMode.Trusted,
            _ => PermissionMode.Approval
        };
    }

    private static ToolRisk MapRisk(string risk)
    {
        return risk.ToLowerInvariant() switch
        {
            "read-only" => ToolRisk.ReadOnly,
            "workspace-write" => ToolRisk.WriteFile,
            "shell" => ToolRisk.Shell,
            "git-write" => ToolRisk.GitWrite,
            "external-url" => ToolRisk.ExternalUrl,
            _ => ToolRisk.ExternalUrl
        };
    }
}
