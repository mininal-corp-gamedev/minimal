using Sandbox;
using System;

public sealed class TriggerBuilding : Component, Component.ITriggerListener
{
    [Property] public List<JobDefinition> AllowedJobs { get; set; } = new();

    void ITriggerListener.OnTriggerEnter(GameObject other)
    {
#if SERVER
        if (!Networking.IsHost) return;
        if (!other.Components.TryGet<PropCustom>(out var prop, FindMode.EverythingInSelfAndParent)) return;

        prop.SetTriggerBuilding(this);
        CheckProp(prop, true);
#endif
    }

    void ITriggerListener.OnTriggerExit(GameObject other)
    {
#if SERVER
        if (!Networking.IsHost) return;
        if (!other.Components.TryGet<PropCustom>(out var prop, FindMode.EverythingInSelfAndParent)) return;

        prop.ClearTriggerBuilding(this);
#endif
    }

    public bool CheckProp(PropCustom prop, bool notifyOwner)
    {
#if SERVER
        if (!Networking.IsHost) return false;
        if (!prop.IsValid() || !prop.GameObject.IsValid()) return false;

        var owner = prop.PlayerOwner;
        if (IsJobAllowed(owner))
            return true;

        if (notifyOwner)
            owner?.NotifyPropBuildingForbidden();

        prop.GameObject.Destroy();
        return false;
#else
        return false;
#endif
    }

    public bool IsJobAllowed(Player player)
    {
        if (!player.IsValid()) return false;
        if (AllowedJobs is null || AllowedJobs.Count == 0) return false;

        var jobId = player.Job?.JobId;
        if (string.IsNullOrWhiteSpace(jobId)) return false;

        foreach (var allowedJob in AllowedJobs)
        {
            if (string.Equals(allowedJob?.Id, jobId, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
