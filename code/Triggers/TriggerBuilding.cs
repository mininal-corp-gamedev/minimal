using Sandbox;
using System;

public sealed class TriggerBuilding : Component, Component.ITriggerListener
{
    [Property] public List<JobDefinition> AllowedJobs { get; set; } = new();

    void ITriggerListener.OnTriggerEnter(GameObject other)
    {
        if (!Networking.IsHost) return;
        if (!other.Components.TryGet<PropCustom>(out var prop, FindMode.EverythingInSelfAndParent)) return;

        CheckProp(prop);
    }

    [Rpc.Host]
    private void CheckProp(PropCustom prop)
    {
        if (!Networking.IsHost) return;
        if (Rpc.Caller != prop.PlayerOwner.Network.Owner) return;

        var owner = prop.PlayerOwner;
        if (!IsJobAllowed(owner))
            prop.GameObject.Destroy();
    }

    private bool IsJobAllowed(Player player)
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
